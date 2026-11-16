// The end of the script that converts one hymn's ABC to MEI with abc2svg (npm abc2svg@1.22.1, LGPL/GPL, used only by the
// Library tool): Hymns.cs writes abc2svg-1.js, cmdline.js and tomei.js from the package one after another, then this, into one
// file beside node_modules, as the package's own abcnode script does, and runs node on it for each hymn. The MEI goes to stdout,
// abc2svg's warnings to stderr. Run as:
//   node abc-to-mei.js <hymn.abc>
//
// What abcnode adds after the backend, with the package's folder found beside this file instead of written in.
var path = require('path').join(__dirname, 'node_modules', 'abc2svg') + '/'

abc2svg.print = function(str) {
	process.stdout.write(str + '\n')
}
abc2svg.printErr = function(str) {
	process.stderr.write(str + '\n')
}
abc2svg.quit = function() {
	process.exit(1)
}
abc2svg.readFile = function(fname) {
	var fs = require('fs')
	try {
		return fs.readFileSync(fname).toString()
	} catch (e) {
	}
	return null
}
abc2svg.get_mtime = function(fn) {
	return require('fs').statSync(fn).mtime
}
abc2svg.loadjs = function(fn, relay, onerror) {
	try {
		var geval = eval
		geval(abc2svg.readFile(path + fn))
		if (relay)
			relay()
	} catch (e) {
		if (onerror)
			onerror()
		else
			abc2svg.printErr('Cannot read file ' + fn)
	}
}

// tomei.js was written for an earlier core, which ended a syllable's text with a newline where a hyphen followed it ("A-
// bide"). This core marks it with ln = 1 instead (and a lone "-" or "_" that holds a syllable over more notes with ln = 2), so
// tomei would write each half as a word of its own. While tomei writes the tune, each such syllable has the newline it looks
// for: then it writes con="d" (a hyphen to the next syllable) and leaves the lone ones out. A verse with no syllable on a note
// where a later verse has one has an empty one meanwhile, which tomei also leaves out, rather than stopping. Afterwards the core
// draws the tune (for nothing), so each syllable gets its own text back, which carries its measured width.
var tomeiInit = abc2svg.abc_init
abc2svg.abc_init = function(args) {
	tomeiInit(args)
	var glue = abc.set_sym_glue
	abc.set_sym_glue = function(width) {
		var changed = []
		for (var s = abc.get_tsfirst(); s; s = s.ts_next) {
			if (!s.a_ly)
				continue
			for (var i = 0; i < s.a_ly.length; i++) {
				var ly = s.a_ly[i]
				if (!ly) {
					changed.push({ s: s, i: i })
					s.a_ly[i] = { t: '-\n' }
				} else if (ly.ln && ly.t.slice(-1) != '\n') {
					changed.push({ s: s, i: i, t: ly.t })
					ly.t = ly.t + '\n'
				}
			}
		}
		var done = glue(width)
		changed.forEach(function(c) {
			if (c.t === undefined)
				delete c.s.a_ly[c.i]
			else
				c.s.a_ly[c.i].t = c.t
		})
		return done
	}
}

global.Abc = Abc
global.abc2svg = abc2svg

var args = process.argv
args.shift()
args.shift()
global.user = user
abc_cmd('abcnode', args, 'nodeJS')
