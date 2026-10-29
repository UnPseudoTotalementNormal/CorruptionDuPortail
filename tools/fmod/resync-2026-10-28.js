// One-off, 2026-10-28: bring FMODProject back level with the committed banks. From 2025-10 to 2026-02 the banks were
// built from a copy of the project that was never committed (light buttons, message menu and card sounds, menu music),
// so Master.bank held 9 events this project lacked, and a few renames. Their audio was recovered from the old
// Master.bank with tools/fmod/banks.py (recover = what each event plays, reverb tail included; the menu music = its
// sample decoded from the bank, volume measured on a render, loop points read from the event's timeline position).
// After this script: tools/fmod/banks.py setguid for each created event (the game references events by GUID), then build all banks and
// tools/fmod/banks.py compare against the old banks.
// Run: tools/fmod/fmod.sh script tools/fmod/resync-2026-10-28.js
var project = studio.project;
var fp = project.filePath.split("\\").join("/");
var assetsDir = fp.substring(0, fp.lastIndexOf("/") + 1) + "Assets/Recovered/";

function need(path) {
    var o = project.lookup(path);
    if (!o) { throw new Error("not found: " + path); }
    return o;
}

// Renames done in the uncommitted project.
var brand = project.lookup("event:/Characters/Mage Occulte/corrupting brand");
if (brand) { brand.name = "Corrupting brand"; console.log("renamed: Corrupting brand"); }
var messages = project.lookup("event:/Interface/Messages");
if (messages) { messages.name = "Messages General + Anomaly"; console.log("renamed: Messages General + Anomaly"); }
var waiting = project.lookup("event:/Interface/Messages General + Anomaly/WaitingforPower");
if (waiting) { waiting.folder = need("event:/Interface"); console.log("moved: event:/Interface/WaitingforPower"); }

var master = need("bank:/Master");
var sfx = need("bus:/Sfx");
var music = need("bus:/Music");
var events = [
    // [folder, name, file, bus, volume dB, loop start s, loop end s]
    ["event:/Interface", "LightOffButton", "LightOffButton.wav", sfx, 0],
    ["event:/Interface", "LightOnButton", "LightOnButton.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Menu 1", "Menu 1.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Menu 2", "Menu 2.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Send Message 2", "Send Message 2.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Touch Card 1", "Touch Card 1.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Touch Card 2", "Touch Card 2.wav", sfx, 0],
    ["event:/Interface/Messages General + Anomaly", "Touch Card 3", "Touch Card 3.wav", sfx, 0],
    ["event:/Interface", "MenuMusic", "La Petite Fille De La Mer.ogg", music, -15, 0.7, 184.5],
];
for (var i = 0; i < events.length; i++) {
    var e = events[i];
    var path = e[0] + "/" + e[1];
    if (project.lookup(path)) { console.log("exists: " + path); continue; }
    var audio = project.importAudioFile(assetsDir + e[2]);
    if (!audio) { throw new Error("import failed: " + e[2]); }
    var event = project.create("Event");
    event.name = e[1];
    event.folder = need(e[0]);
    var track = event.addGroupTrack("Audio");
    var sound = track.addSound(event.timeline, "SingleSound", 0, audio.length);
    sound.audioFile = audio;
    sound.volume = e[4];
    if (e.length > 5) {
        audio.isStreaming = true;
        var loop = project.create("LoopRegion");
        loop.position = e[5];
        loop.length = e[6] - e[5];
        loop.timeline = event.timeline;
        loop.markerTrack = event.markerTracks[0];
    }
    event.mixerInput.output = e[3];
    event.relationships.banks.add(master);
    console.log("created: " + path + " " + event.id);
}
project.save();
console.log("saved");
