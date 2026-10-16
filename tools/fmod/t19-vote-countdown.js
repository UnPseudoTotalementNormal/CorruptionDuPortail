// Board task T19: the vote candle and its clock. Imports the placeholder sounds of FMODProject/Corrupted/Assets/T19
// and makes one 2D one-shot event per sound in event:/Interface, in their own bank "VoteCandle". Safe to run again.
// Own bank, built alone: the committed Master.bank holds events missing from this project (the menu music of
// 2026-02-09 was built from a project never committed), so rebuilding Master from here would delete them.
// Run: tools/fmod/fmod.sh script tools/fmod/t19-vote-countdown.js, then tools/fmod/fmod.sh build VoteCandle.
var project = studio.project;
var fp = project.filePath.split("\\").join("/");
var assetsDir = fp.substring(0, fp.lastIndexOf("/") + 1) + "Assets/T19/";
var folder = project.lookup("event:/Interface");
var bank = project.lookup("bank:/VoteCandle");
if (!bank) {
    bank = project.create("Bank");
    bank.name = "VoteCandle";
    console.log("created: bank:/VoteCandle");
}
var master = project.lookup("bank:/Master");
if (!folder) { throw new Error("event:/Interface not found"); }

var sounds = [
    ["VoteClockTic", "vote_clock_tic.wav"],
    ["VoteClockTac", "vote_clock_tac.wav"],
    ["VoteCandleSnuff", "vote_candle_snuff.wav"],
    ["VoteCandleIgnite", "vote_candle_ignite.wav"],
];

for (var i = 0; i < sounds.length; i++) {
    var name = sounds[i][0];
    var existing = project.lookup("event:/Interface/" + name);
    if (existing) {
        // Earlier runs put them in Master: move them to their own bank.
        if (master) { existing.relationships.banks.remove(master); }
        existing.relationships.banks.add(bank);
        console.log("exists: " + name + " (bank VoteCandle)");
        continue;
    }
    var audio = project.importAudioFile(assetsDir + sounds[i][1]);
    if (!audio) { throw new Error("import failed: " + sounds[i][1]); }
    var event = project.create("Event");
    event.name = name;
    event.folder = folder;
    var track = event.addGroupTrack("Audio");
    var sound = track.addSound(event.timeline, "SingleSound", 0, audio.length);
    sound.audioFile = audio;
    event.relationships.banks.add(bank);
    console.log("created: event:/Interface/" + name + " (" + audio.length + " s)");
}
project.save();
console.log("saved");
