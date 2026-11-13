package com.saphyiere.prima.conversion;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.RectF;
import android.graphics.pdf.PdfRenderer;
import android.os.Build;
import android.os.ParcelFileDescriptor;
import android.util.Log;
import androidx.javascriptengine.IsolateStartupParameters;
import androidx.javascriptengine.JavaScriptIsolate;
import androidx.javascriptengine.JavaScriptSandbox;
import com.caverock.androidsvg.SVG;
import com.google.common.util.concurrent.ListenableFuture;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.security.SecureRandom;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

/** One cancellable operation. Its .NET owner serializes operations and uses a fresh instance for each call. */
public final class NativeRenderer {
    private static final String TAG = "PrimaNativeRenderer";
    private final Context context;
    private final Object lifecycle = new Object();
    private volatile boolean cancelled;
    private JavaScriptSandbox activeSandbox;
    private Thread activeThread;

    public NativeRenderer(Context context) {
        this.context = context.getApplicationContext();
    }

    /** Exercises the same Wasm, SVG and PNG path used by conversion before any paid request is sent. */
    public String checkSupport() {
        if (Build.VERSION.SDK_INT < 26) return "Background PDF conversion requires Android 8 or later.";
        try {
            if (!JavaScriptSandbox.isSupported()) return "Background PDF conversion is unavailable with this device's Android System WebView. Update Android System WebView and try again.";
            engrave("<mei xmlns=\"http://www.music-encoding.org/ns/mei\" meiversion=\"5.0\"><music><body><mdiv><score>"
                + "<scoreDef meter.count=\"4\" meter.unit=\"4\"><staffGrp><staffDef n=\"1\" lines=\"5\" clef.shape=\"G\" clef.line=\"2\"/></staffGrp></scoreDef>"
                + "<section><measure n=\"1\"><staff n=\"1\"><layer n=\"1\"><note pname=\"c\" oct=\"4\" dur=\"1\"/></layer></staff></measure></section>"
                + "</score></mdiv></body></music></mei>", 256);
            return null;
        } catch (Exception error) {
            Throwable cause = error;
            while (cause.getCause() != null) cause = cause.getCause();
            if (cause instanceof java.util.concurrent.TimeoutException) return "The background renderer did not start in time. Restart Prima and try again.";
            String detail = cause.getMessage();
            return "Background PDF conversion rendering is unavailable: " + (detail == null || detail.trim().isEmpty() ? cause.getClass().getSimpleName() : detail);
        }
    }

    public byte[] engrave(String mei, int width) throws Exception {
        if (mei == null || mei.trim().isEmpty()) throw new IllegalArgumentException("The notation is empty.");
        if (width < 1 || width > 4096) throw new IllegalArgumentException("Image width must be between 1 and 4096 pixels.");
        synchronized (lifecycle) {
            if (cancelled) throw new InterruptedException("Rendering was cancelled.");
            activeThread = Thread.currentThread();
        }
        File library = null;
        JavaScriptSandbox sandbox = null;
        try {
            if (Build.VERSION.SDK_INT < 26 || !JavaScriptSandbox.isSupported()) throw new UnsupportedOperationException("The background JavaScript engine is unavailable.");
            ListenableFuture<JavaScriptSandbox> opening = JavaScriptSandbox.createConnectedInstanceAsync(context);
            try {
                sandbox = opening.get(30, TimeUnit.SECONDS);
            } catch (Exception error) {
                // Connection can finish after a timeout or cancellation. Close that late result instead of leaking the
                // one sandbox allowed per application. Cancelling only the Future does not establish this cleanup.
                opening.addListener(() -> {
                    try { opening.get().close(); }
                    catch (Exception cleanupError) { Log.w(TAG, "The abandoned engine connection did not produce a sandbox.", cleanupError); }
                }, Runnable::run);
                throw error;
            }
            synchronized (lifecycle) {
                if (cancelled) throw new InterruptedException("Rendering was cancelled.");
                activeSandbox = sandbox;
            }
            for (String feature : new String[] {
                JavaScriptSandbox.JS_FEATURE_WASM_COMPILATION,
                JavaScriptSandbox.JS_FEATURE_PROMISE_RETURN,
                JavaScriptSandbox.JS_FEATURE_PROVIDE_CONSUME_ARRAY_BUFFER,
                JavaScriptSandbox.JS_FEATURE_EVALUATE_FROM_FD,
                JavaScriptSandbox.JS_FEATURE_EVALUATE_WITHOUT_TRANSACTION_LIMIT
            }) {
                if (!sandbox.isFeatureSupported(feature)) throw new UnsupportedOperationException("The Android WebView engine lacks " + feature + ". Update Android System WebView and try again.");
            }
            library = File.createTempFile("prima-verovio-", ".js", context.getCacheDir());
            try (InputStream input = context.getAssets().open("conversion/verovio.js"); FileOutputStream output = new FileOutputStream(library)) {
                byte[] buffer = new byte[32768];
                int count;
                while ((count = input.read(buffer)) != -1) {
                    if (cancelled) throw new InterruptedException("Rendering was cancelled.");
                    output.write(buffer, 0, count);
                }
            }
            IsolateStartupParameters parameters = new IsolateStartupParameters();
            if (sandbox.isFeatureSupported(JavaScriptSandbox.JS_FEATURE_ISOLATE_MAX_HEAP_SIZE)) parameters.setMaxHeapSizeBytes(256L * 1024 * 1024);
            try (JavaScriptIsolate isolate = sandbox.createIsolate(parameters)) {
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
                String script = "new Promise((resolve,reject)=>{const run=()=>{let tk;try{tk=new verovio.toolkit();"
                    + "tk.setOptions({xmlIdChecksum:true,font:'Leipzig',scale:100,pageWidth:2000,pageHeight:60000,adjustPageHeight:true,breaks:'encoded',minLastJustification:0,header:'none',footer:'none',pageMarginLeft:24,pageMarginRight:24,pageMarginTop:24,pageMarginBottom:24,svgViewBox:true,svgHtml5:true});"
                    + "if(!tk.loadData(" + JSONObject.quote(mei) + "))throw Error('Verovio rejected the notation');"
                    + "if(tk.getPageCount()!==1)throw Error('The comparison notation does not fit on one page');"
                    + "resolve(tk.renderToSVG(1));}catch(e){reject(String(e.stack||e));}finally{if(tk)tk.destroy();}};"
                    + "if(verovio.module.calledRun)run();else verovio.module.onRuntimeInitialized=run;})";
                String svg = isolate.evaluateJavaScriptAsync(script).get(60, TimeUnit.SECONDS);
                if (cancelled) throw new InterruptedException("Rendering was cancelled.");
                if (!svg.contains("<svg") || !svg.contains("class=\"measure\"")) throw new IllegalStateException("The notation renderer returned no measures.");
                SVG nativeSvg = SVG.getFromString(svg);
                RectF box = nativeSvg.getDocumentViewBox();
                if (box == null || box.width() <= 0 || box.height() <= 0) throw new IllegalStateException("The notation image has no usable viewBox.");
                double exactHeight = width * (double) box.height() / box.width();
                if (!Double.isFinite(exactHeight) || exactHeight > 20_000_000d / width) throw new IllegalArgumentException("The notation image is too large; convert fewer bars at once.");
                int height = Math.max(1, (int) Math.round(exactHeight));
                Bitmap image = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                try {
                    Canvas canvas = new Canvas(image);
                    canvas.drawColor(Color.WHITE);
                    nativeSvg.setDocumentWidth(width);
                    nativeSvg.setDocumentHeight(height);
                    nativeSvg.renderToCanvas(canvas);
                    return png(image);
                } finally { image.recycle(); }
            }
        } finally {
            synchronized (lifecycle) {
                activeSandbox = null;
                activeThread = null;
            }
            if (sandbox != null) sandbox.close();
            if (library != null && !library.delete()) Log.w(TAG, "Could not remove temporary renderer asset.");
            // ThreadPool workers are reused by .NET. Do not leave a Java interruption on the next operation's thread.
            Thread.interrupted();
        }
    }

    public byte[] crop(byte[] pdf, int pageNumber, double x, double y, double width, double height, int targetWidth) throws Exception {
        if (pdf == null || pdf.length == 0) throw new IllegalArgumentException("The source PDF is empty.");
        if (pageNumber < 1) throw new IllegalArgumentException("PDF page numbers start at one.");
        if (!Double.isFinite(x) || !Double.isFinite(y) || !Double.isFinite(width) || !Double.isFinite(height)
            || x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1.000001 || y + height > 1.000001)
            throw new IllegalArgumentException("The source crop is outside its page.");
        if (targetWidth < 1 || targetWidth > 4096) throw new IllegalArgumentException("Image width must be between 1 and 4096 pixels.");
        synchronized (lifecycle) {
            if (cancelled) throw new InterruptedException("Rendering was cancelled.");
            activeThread = Thread.currentThread();
        }
        File source = null;
        try {
            source = File.createTempFile("prima-source-", ".pdf", context.getCacheDir());
            try (FileOutputStream output = new FileOutputStream(source)) { output.write(pdf); }
            if (cancelled) throw new InterruptedException("Rendering was cancelled.");
            try (ParcelFileDescriptor descriptor = ParcelFileDescriptor.open(source, ParcelFileDescriptor.MODE_READ_ONLY);
                 PdfRenderer renderer = new PdfRenderer(descriptor)) {
                if (pageNumber > renderer.getPageCount()) throw new IllegalArgumentException("The requested page is absent from the source PDF.");
                try (PdfRenderer.Page page = renderer.openPage(pageNumber - 1)) {
                    double scale = targetWidth / (page.getWidth() * width);
                    double exactHeight = page.getHeight() * height * scale;
                    if (!Double.isFinite(exactHeight) || exactHeight > 20_000_000d / targetWidth) throw new IllegalArgumentException("The source crop is too large.");
                    int targetHeight = Math.max(1, (int) Math.round(exactHeight));
                    Bitmap image = Bitmap.createBitmap(targetWidth, targetHeight, Bitmap.Config.ARGB_8888);
                    try {
                        image.eraseColor(Color.WHITE);
                        Matrix transform = new Matrix();
                        transform.setValues(new float[] {(float) scale, 0, (float) (-x * page.getWidth() * scale),
                            0, (float) scale, (float) (-y * page.getHeight() * scale), 0, 0, 1});
                        page.render(image, null, transform, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                        return png(image);
                    } finally { image.recycle(); }
                }
            }
        } finally {
            synchronized (lifecycle) { activeThread = null; }
            if (source != null && !source.delete()) Log.w(TAG, "Could not remove temporary source PDF.");
            Thread.interrupted();
        }
    }

    private byte[] png(Bitmap image) throws Exception {
        if (cancelled) throw new InterruptedException("Rendering was cancelled.");
        try (ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            if (!image.compress(Bitmap.CompressFormat.PNG, 100, output)) throw new IllegalStateException("PNG compression failed.");
            if (cancelled) throw new InterruptedException("Rendering was cancelled.");
            return output.toByteArray();
        }
    }

    public void cancel() {
        synchronized (lifecycle) {
            cancelled = true;
            // Closing the sandbox terminates Wasm even when individual-isolate termination is unsupported.
            if (activeSandbox != null) {
                try { activeSandbox.close(); }
                catch (RuntimeException error) { Log.w(TAG, "The cancelled engine was already unavailable.", error); }
                if (activeThread != null) activeThread.interrupt();
            }
            // An in-progress bind has no closeable sandbox yet. Let it resolve (within its connection timeout), then
            // engrave's cancelled check and finally close it before .NET releases the application's renderer gate.
            // Interrupting that Future here would let the next call race a still-bound, not-yet-closeable connection.
        }
    }
}
