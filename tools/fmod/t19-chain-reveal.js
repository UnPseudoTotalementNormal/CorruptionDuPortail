// Board task T19, points 2-4: the chained cat lifted then flattened. Imports the placeholder sounds of
// FMODProject/Corrupted/Assets/T19 and makes one 2D one-shot event per sound in event:/Interface, in their own bank
// "ChainReveal" (built alone, like VoteCandle: the committed Master.bank holds events missing from this project).
// Run: tools/fmod/fmod.sh script tools/fmod/t19-chain-reveal.js, then tools/fmod/fmod.sh build (every bank).
var project = studio.project;
var fp = project.filePath.split("\\").join("/");
var assetsDir = fp.substring(0, fp.lastIndexOf("/") + 1) + "Assets/T19/";
var folder = project.lookup("event:/Interface");
if (!folder) { throw new Error("event:/Interface not found"); }
var bank = project.lookup("bank:/ChainReveal");
if (!bank) {
    bank = project.create("Bank");
    bank.name = "ChainReveal";
    console.log("created: bank:/ChainReveal");
}
var sounds = [
    ["ChainLift", "chain_lift.wav"],
    ["ChainSplat", "chain_splat.wav"],
];
for (var i = 0; i < sounds.length; i++) {
    var name = sounds[i][0];
    if (project.lookup("event:/Interface/" + name)) { console.log("exists: " + name); continue; }
    var audio = project.importAudioFile(assetsDir + sounds[i][1]);
    if (!audio) { throw new Error("import failed: " + sounds[i][1]); }
    var event = project.create("Event");
    event.name = name;
    event.folder = folder;
    var track = event.addGroupTrack("Audio");
    var sound = track.addSound(event.timeline, "SingleSound", 0, audio.length);
    sound.audioFile = audio;
    event.relationships.banks.add(bank);
    console.log("created: event:/Interface/" + name + " " + event.id + " (" + audio.length + " s)");
}
project.save();
console.log("saved");
