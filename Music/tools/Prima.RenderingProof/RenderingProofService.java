package com.saphyiere.prima.renderingproof;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.RectF;
import android.graphics.pdf.PdfRenderer;
import android.os.Build;
import android.os.IBinder;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.os.PowerManager;
import android.util.Log;
import androidx.javascriptengine.IsolateStartupParameters;
import androidx.javascriptengine.JavaScriptIsolate;
import androidx.javascriptengine.JavaScriptSandbox;
import com.caverock.androidsvg.SVG;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import org.json.JSONArray;
import org.json.JSONObject;

public final class RenderingProofService extends Service {
    private static final String TAG = "PrimaRenderingProof";
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private int latestStartId;
    private volatile boolean running;
    private volatile boolean cancelled;
    private volatile JavaScriptIsolate activeIsolate;
    private volatile Thread activeThread;

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        latestStartId = startId;
        if (intent != null && "com.saphyiere.prima.renderingproof.CANCEL".equals(intent.getAction())) {
            requestCancel();
            if (!running) stopSelf(startId);
            return START_NOT_STICKY;
        }
        if (running) return START_NOT_STICKY;
        running = true;
        cancelled = false;
        try {
            NotificationManager manager = getSystemService(NotificationManager.class);
            manager.createNotificationChannel(new NotificationChannel("proof", "Rendering proof", NotificationManager.IMPORTANCE_LOW));
            Notification notification = new Notification.Builder(this, "proof")
                .setSmallIcon(android.R.drawable.ic_menu_info_details)
                .setContentTitle("Prima rendering proof")
                .setContentText("Testing rendering without an Activity")
                .setOngoing(true).build();
            if (Build.VERSION.SDK_INT >= 29) startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
            else startForeground(1, notification);
        } catch (RuntimeException error) {
            running = false;
            File folder = new File(getExternalFilesDir(null), "proof");
            try {
                if (!folder.isDirectory()) folder.mkdirs();
                JSONObject failure = new JSONObject().put("status", "failed").put("phase", "foreground-start")
                    .put("error", Log.getStackTraceString(error)).put("serviceStopAccepted", stopSelfResult(latestStartId));
                writeJson(folder, failure);
            } catch (Exception ignored) { stopSelf(); }
            Log.e(TAG, "PROOF_FOREGROUND_START_FAILED", error);
            return START_NOT_STICKY;
        }
        boolean requireScreenOff = intent != null && intent.getBooleanExtra("require_screen_off", false);
        boolean cancellationProbe = intent != null && intent.getBooleanExtra("cancellation_probe", false);
        worker.execute(() -> runProof(requireScreenOff, cancellationProbe));
        return START_NOT_STICKY;
    }

    private void runProof(boolean requireScreenOff, boolean cancellationProbe) {
        activeThread = Thread.currentThread();
        File folder = new File(getExternalFilesDir(null), "proof");
        JSONObject result = new JSONObject();
        PowerManager power = getSystemService(PowerManager.class);
        PowerManager.WakeLock awake = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, TAG + ":cpu");
        awake.acquire(300_000);
        try {
            if (cancelled) throw new InterruptedException("Cancelled before work started.");
            if (!folder.isDirectory() && !folder.mkdirs()) throw new IllegalStateException("Cannot create proof output folder.");
            android.content.pm.ActivityInfo[] activities = getPackageManager().getPackageInfo(getPackageName(), android.content.pm.PackageManager.GET_ACTIVITIES).activities;
            result.put("status", "running").put("declaredActivityCount", activities == null ? 0 : activities.length).put("context", getClass().getName()).put("requireScreenOff", requireScreenOff)
                .put("sdk", Build.VERSION.SDK_INT).put("startedAtMs", System.currentTimeMillis());
            writeJson(folder, result);
            if (requireScreenOff) {
                Log.i(TAG, "WAITING_FOR_SCREEN_OFF");
                long until = System.currentTimeMillis() + 120_000;
                while (power.isInteractive() && System.currentTimeMillis() < until) Thread.sleep(500);
                if (power.isInteractive()) throw new IllegalStateException("The screen remained on; screen-off proof was not run.");
            }
            result.put("screenOffAtStart", !power.isInteractive());
            {
                File input = new File(getExternalFilesDir(null), "input.pdf");
                boolean externalPdf = input.isFile();
                if (!input.isFile()) {
                    input = new File(folder, "synthetic.pdf");
                    copyAsset("input.pdf", input);
                }
                result.put("pdfSource", externalPdf ? "external" : "synthetic")
                    .put("pdfSourceSha256", hex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(input.toPath()))));
                try (ParcelFileDescriptor descriptor = ParcelFileDescriptor.open(input, ParcelFileDescriptor.MODE_READ_ONLY);
                     PdfRenderer renderer = new PdfRenderer(descriptor);
                     PdfRenderer.Page page = renderer.openPage(0)) {
                    int width = 900;
                    int height = Math.round(width * (float) page.getHeight() / page.getWidth());
                    Bitmap preview = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                    new Canvas(preview).drawColor(Color.WHITE);
                    page.render(preview, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                    savePng(preview, new File(folder, "pdf-native.png"));
                    int[] pixels = new int[width * height];
                    preview.getPixels(pixels, 0, width, 0, 0, width, height);
                    List<Double> lines = new ArrayList<>();
                    boolean[] darkRows = new boolean[height];
                    for (int y = 0; y < height; y++) {
                        int dark = 0;
                        for (int x = 0; x < width; x++) {
                            int pixel = pixels[y * width + x];
                            if (Color.red(pixel) + Color.green(pixel) + Color.blue(pixel) < 3 * 240) dark++;
                        }
                        darkRows[y] = dark > width / 2;
                    }
                    for (int y = 0; y < height; y++) {
                        if (!darkRows[y]) continue;
                        int first = y;
                        while (y + 1 < height && darkRows[y + 1]) y++;
                        lines.add((first + y) / 2.0);
                    }
                    List<double[]> staves = new ArrayList<>();
                    for (int i = 0; i + 4 < lines.size();) {
                        double space = (lines.get(i + 4) - lines.get(i)) / 4;
                        boolean even = space > 0;
                        for (int k = i; k < i + 4; k++) even &= Math.abs(lines.get(k + 1) - lines.get(k) - space) <= Math.max(1.5, 0.2 * space);
                        if (even) {
                            staves.add(new double[] { lines.get(i), lines.get(i + 4), space });
                            i += 5;
                        } else i++;
                    }
                    List<double[]> groups = new ArrayList<>();
                    for (int i = 0; i < staves.size(); i += 2) groups.add(new double[] {
                        staves.get(i)[0], staves.get(Math.min(i + 1, staves.size() - 1))[1], staves.get(i)[2]
                    });
                    if (groups.size() != 2) throw new IllegalStateException("Expected 2 fixture systems, found " + groups.size());
                    int fullWidth = 2576;
                    int fullHeight = Math.round(fullWidth * (float) page.getHeight() / page.getWidth());
                    Bitmap full = Bitmap.createBitmap(fullWidth, fullHeight, Bitmap.Config.ARGB_8888);
                    new Canvas(full).drawColor(Color.WHITE);
                    page.render(full, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                    JSONArray regions = new JSONArray();
                    for (int i = 0; i < groups.size(); i++) {
                        double[] group = groups.get(i);
                        double top = i == 0 ? Math.max(0, group[0] - 4 * group[2]) : (groups.get(i - 1)[1] + group[0]) / 2;
                        double bottom = i == groups.size() - 1 ? Math.min(height, group[1] + 4 * group[2]) : (group[1] + groups.get(i + 1)[0]) / 2;
                        int first = Math.max(0, (int) Math.floor(top * fullWidth / width));
                        int last = Math.min(fullHeight, (int) Math.ceil(bottom * fullWidth / width));
                        Bitmap crop = Bitmap.createBitmap(full, 0, first, fullWidth, last - first);
                        int ink = countInk(crop);
                        if (ink < 100) throw new IllegalStateException("Native PDF crop is empty.");
                        savePng(crop, new File(folder, "pdf-crop-" + (i + 1) + ".png"));
                        crop.recycle();
                        regions.put(new JSONObject().put("top", top).put("bottom", bottom).put("inkPixels", ink));
                    }
                    result.put("pdfPages", renderer.getPageCount()).put("pdfWidth", width).put("pdfHeight", height)
                        .put("staffLines", lines.size()).put("pdfRegions", regions);
                    preview.recycle();
                    full.recycle();
                    writeJson(folder, result);
                }
            }
            result.put("screenOffAfterPdf", !power.isInteractive());
            if (!JavaScriptSandbox.isSupported()) throw new IllegalStateException("JavaScriptSandbox is not supported by this device's WebView provider.");
            try (JavaScriptSandbox sandbox = JavaScriptSandbox.createConnectedInstanceAsync(this).get(30, TimeUnit.SECONDS)) {
                JSONArray features = new JSONArray();
                for (String feature : new String[] {
                    JavaScriptSandbox.JS_FEATURE_WASM_COMPILATION,
                    JavaScriptSandbox.JS_FEATURE_PROMISE_RETURN,
                    JavaScriptSandbox.JS_FEATURE_PROVIDE_CONSUME_ARRAY_BUFFER,
                    JavaScriptSandbox.JS_FEATURE_EVALUATE_FROM_FD,
                    JavaScriptSandbox.JS_FEATURE_ISOLATE_MAX_HEAP_SIZE,
                    JavaScriptSandbox.JS_FEATURE_EVALUATE_WITHOUT_TRANSACTION_LIMIT
                }) {
                    if (sandbox.isFeatureSupported(feature)) features.put(feature);
                }
                result.put("features", features);
                writeJson(folder, result);
                for (String feature : new String[] {
                    JavaScriptSandbox.JS_FEATURE_WASM_COMPILATION,
                    JavaScriptSandbox.JS_FEATURE_PROMISE_RETURN,
                    JavaScriptSandbox.JS_FEATURE_PROVIDE_CONSUME_ARRAY_BUFFER,
                    JavaScriptSandbox.JS_FEATURE_EVALUATE_FROM_FD
                }) {
                    if (!sandbox.isFeatureSupported(feature)) throw new IllegalStateException("Required sandbox feature is unavailable: " + feature);
                }
                String mei;
                File externalMei = new File(getExternalFilesDir(null), "input.mei");
                if (externalMei.isFile()) mei = new String(Files.readAllBytes(externalMei.toPath()), StandardCharsets.UTF_8);
                else {
                    try (InputStream source = getAssets().open("fixture.mei")) {
                        mei = new String(source.readAllBytes(), StandardCharsets.UTF_8);
                    }
                }
                result.put("meiSource", externalMei.isFile() ? "external" : "synthetic")
                    .put("meiSourceSha256", hex(MessageDigest.getInstance("SHA-256").digest(mei.getBytes(StandardCharsets.UTF_8))));
                writeJson(folder, result);
                File library = new File(folder, "verovio.js");
                copyAsset("conversion/verovio.js", library);
                JSONArray passes = new JSONArray();
                String firstPng = null;
                for (int pass = 1; pass <= 2; pass++) {
                    IsolateStartupParameters parameters = new IsolateStartupParameters();
                    if (sandbox.isFeatureSupported(JavaScriptSandbox.JS_FEATURE_ISOLATE_MAX_HEAP_SIZE)) parameters.setMaxHeapSizeBytes(256L * 1024 * 1024);
                    try (JavaScriptIsolate isolate = sandbox.createIsolate(parameters)) {
                        activeIsolate = isolate;
                        long started = System.currentTimeMillis();
                        byte[] random = new byte[65536];
                        new SecureRandom().nextBytes(random);
                        isolate.provideNamedData("random", random);
                        String bootstrap = "(async()=>{const bytes=new Uint8Array(await android.consumeNamedDataAsArrayBuffer('random'));let at=0;"
                            + "globalThis.crypto={getRandomValues(view){const out=new Uint8Array(view.buffer,view.byteOffset,view.byteLength);if(at+out.length>bytes.length)throw Error('Random buffer exhausted');out.set(bytes.subarray(at,at+out.length));at+=out.length;return view;}};"
                            + "globalThis.console??={log(){},warn(){},error(){}};return 'ready';})()";
                        isolate.evaluateJavaScriptAsync(bootstrap).get(30, TimeUnit.SECONDS);
                        try (ParcelFileDescriptor descriptor = ParcelFileDescriptor.open(library, ParcelFileDescriptor.MODE_READ_ONLY)) {
                            isolate.evaluateJavaScriptAsync(descriptor).get(60, TimeUnit.SECONDS);
                        }
                        if (cancellationProbe) {
                            result.put("phase", "cancellation-probe");
                            writeJson(folder, result);
                            Log.i(TAG, "CANCEL_PROBE_READY");
                            isolate.evaluateJavaScriptAsync("while (true) {} ").get(180, TimeUnit.SECONDS);
                            throw new IllegalStateException("The cancellation probe unexpectedly returned.");
                        }
                        String script = "new Promise((resolve,reject)=>{const run=()=>{try{const tk=new verovio.toolkit();"
                            + "tk.setOptions({xmlIdChecksum:true,font:'Leipzig',scale:100,pageWidth:2000,pageHeight:60000,adjustPageHeight:true,breaks:'encoded',minLastJustification:0,header:'none',footer:'none',pageMarginLeft:24,pageMarginRight:24,pageMarginTop:24,pageMarginBottom:24,svgViewBox:true,svgHtml5:true});"
                            + "if(!tk.loadData(" + JSONObject.quote(mei) + "))throw Error('Verovio rejected the fixture');"
                            + "const svg=tk.renderToSVG(1);resolve(JSON.stringify({svg,version:tk.getVersion()}));}catch(e){reject(String(e.stack||e));}};"
                            + "if(verovio.module.calledRun)run();else verovio.module.onRuntimeInitialized=run;})";
                        JSONObject rendered = new JSONObject(isolate.evaluateJavaScriptAsync(script).get(60, TimeUnit.SECONDS));
                        String svg = rendered.getString("svg");
                        if (!svg.contains("<svg") || !svg.contains("class=\"measure\"")) throw new IllegalStateException("Verovio returned no rendered measures.");
                        writeBytes(new File(folder, "notation-" + pass + ".svg"), svg.getBytes(StandardCharsets.UTF_8));
                        SVG nativeSvg = SVG.getFromString(svg);
                        RectF box = nativeSvg.getDocumentViewBox();
                        if (box == null || box.width() <= 0 || box.height() <= 0) throw new IllegalStateException("SVG has no usable viewBox.");
                        int width = 1600;
                        int height = Math.max(1, Math.round(width * box.height() / box.width()));
                        Bitmap png = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                        Canvas canvas = new Canvas(png);
                        canvas.drawColor(Color.WHITE);
                        nativeSvg.setDocumentWidth(width);
                        nativeSvg.setDocumentHeight(height);
                        nativeSvg.renderToCanvas(canvas);
                        int ink = countInk(png);
                        if (ink < 100 || ink >= (long) width * height / 2) throw new IllegalStateException("Notation PNG is blank or predominantly solid ink.");
                        savePng(png, new File(folder, "notation-" + pass + ".png"));
                        png.recycle();
                        String pngSha = hex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(new File(folder, "notation-" + pass + ".png").toPath())));
                        if (firstPng == null) firstPng = pngSha;
                        else if (!firstPng.equals(pngSha)) throw new IllegalStateException("Notation pixels changed after recreating the JavaScript isolate.");
                        passes.put(new JSONObject().put("pass", pass).put("version", rendered.getString("version"))
                            .put("width", width).put("height", height).put("inkPixels", ink)
                            .put("svgSha256", hex(MessageDigest.getInstance("SHA-256").digest(svg.getBytes(StandardCharsets.UTF_8)))).put("pngSha256", pngSha)
                            .put("elapsedMs", System.currentTimeMillis() - started).put("screenOff", !power.isInteractive()));
                        result.put("notationPasses", passes);
                        writeJson(folder, result);
                    } finally {
                        activeIsolate = null;
                    }
                }
            }
            result.put("screenOffAtEnd", !power.isInteractive());
            if (requireScreenOff && power.isInteractive()) throw new IllegalStateException("The screen was turned on before the proof completed.");
            result.put("status", "passed").put("finishedAtMs", System.currentTimeMillis());
            writeJson(folder, result);
            Log.i(TAG, "PROOF_PASS " + result);
        } catch (Throwable error) {
            try {
                result.put("status", cancelled ? "cancelled" : "failed").put("error", Log.getStackTraceString(error)).put("finishedAtMs", System.currentTimeMillis());
                writeJson(folder, result);
            } catch (Exception ignored) { }
            if (cancelled) Log.i(TAG, "PROOF_CANCELLED");
            else Log.e(TAG, "PROOF_FAIL", error);
        } finally {
            if (awake.isHeld()) awake.release();
            activeThread = null;
            try {
                result.put("wakeLockHeldAfterCleanup", awake.isHeld()).put("activeIsolateAfterCleanup", activeIsolate != null);
                writeJson(folder, result);
            } catch (Exception ignored) { }
            // Complete on the main looper, serialized with onStartCommand. CANCEL and duplicate starts also advance
            // Android's start ID, so stopping against the original run's ID would leave an idle Service alive.
            main.post(() -> {
                running = false;
                stopForeground(STOP_FOREGROUND_REMOVE);
                boolean stopped = stopSelfResult(latestStartId);
                try {
                    result.put("serviceStopAccepted", stopped);
                    writeJson(folder, result);
                } catch (Exception error) { Log.e(TAG, "Could not record Service cleanup.", error); }
                Log.i(TAG, "SERVICE_STOP_ACCEPTED " + stopped);
            });
        }
    }

    private static int countInk(Bitmap bitmap) {
        int[] row = new int[bitmap.getWidth()];
        int count = 0;
        for (int y = 0; y < bitmap.getHeight(); y++) {
            bitmap.getPixels(row, 0, row.length, 0, y, row.length, 1);
            for (int pixel : row) if (Color.red(pixel) + Color.green(pixel) + Color.blue(pixel) < 3 * 240) count++;
        }
        return count;
    }

    private void copyAsset(String name, File target) throws Exception {
        try (InputStream source = getAssets().open(name); FileOutputStream output = new FileOutputStream(target)) { source.transferTo(output); }
    }

    private static void savePng(Bitmap bitmap, File path) throws Exception {
        try (FileOutputStream output = new FileOutputStream(path)) {
            if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)) throw new IllegalStateException("PNG compression failed.");
        }
    }

    private static void writeBytes(File path, byte[] bytes) throws Exception {
        try (FileOutputStream output = new FileOutputStream(path)) { output.write(bytes); }
    }

    private static void writeJson(File folder, JSONObject value) throws Exception {
        writeBytes(new File(folder, "result.json"), value.toString(2).getBytes(StandardCharsets.UTF_8));
    }

    private static String hex(byte[] bytes) {
        StringBuilder output = new StringBuilder();
        for (byte value : bytes) output.append(String.format("%02x", value & 255));
        return output.toString();
    }

    @Override public void onDestroy() {
        if (running) requestCancel();
        worker.shutdownNow();
        super.onDestroy();
    }

    private void requestCancel() {
        cancelled = true;
        JavaScriptIsolate isolate = activeIsolate;
        if (isolate != null) {
            try { isolate.close(); }
            catch (RuntimeException error) { Log.w(TAG, "Isolate was already unavailable during cancellation.", error); }
        }
        Thread thread = activeThread;
        if (thread != null) thread.interrupt();
        Log.i(TAG, "CANCEL_REQUESTED");
    }
}
