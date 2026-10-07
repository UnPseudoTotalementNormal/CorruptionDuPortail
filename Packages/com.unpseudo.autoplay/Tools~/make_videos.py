"""Turns the frames filmed by -autoplay-video into one video.mp4 per process folder (real-time paced), then removes them.

    python Tools~/make_videos.py <run folder>     (a single-process run folder, or a multi-process parent)

Each <process>/video/ holds f000000.jpg… and frames.txt (ffmpeg concat list with each frame's real duration). A process
killed on purpose (crash-at) never wrote frames.txt: its frames are assembled at the fixed rate of video/fps.txt.
Needs ffmpeg (PATH, AUTOPLAY_FFMPEG, or the winget Gyan.FFmpeg install). Prints one line per video.
"""
import glob
import os
import shutil
import subprocess
import sys


def find_ffmpeg():
    candidates = [os.environ.get("AUTOPLAY_FFMPEG"), shutil.which("ffmpeg")]
    local = os.environ.get("LOCALAPPDATA", "")
    candidates += glob.glob(os.path.join(local, "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg*", "*", "bin", "ffmpeg.exe"))
    return next((c for c in candidates if c and os.path.exists(c)), None)


def make(video_dir, ffmpeg):
    frames = sorted(glob.glob(os.path.join(video_dir, "f*.jpg")))
    if not frames:
        return None, "no frames"
    out = os.path.join(os.path.dirname(video_dir), "video.mp4")
    even = "pad=ceil(iw/2)*2:ceil(ih/2)*2"  # x264 needs even sizes
    listing = os.path.join(video_dir, "frames.txt")
    if os.path.exists(listing):
        cmd = [ffmpeg, "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing,
               "-vf", even, "-r", "30", "-c:v", "libx264", "-pix_fmt", "yuv420p", out]
    else:
        fps = open(os.path.join(video_dir, "fps.txt"), encoding="utf-8").read().strip() if os.path.exists(os.path.join(video_dir, "fps.txt")) else "10"
        cmd = [ffmpeg, "-y", "-loglevel", "error", "-framerate", fps, "-i", os.path.join(video_dir, "f%06d.jpg"),
               "-vf", even, "-c:v", "libx264", "-pix_fmt", "yuv420p", out]
    done = subprocess.run(cmd, cwd=video_dir, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if done.returncode != 0 or not os.path.exists(out):
        return None, (done.stderr or "ffmpeg failed").strip()[:300]
    shutil.rmtree(video_dir, ignore_errors=True)
    return out, f"{len(frames)} frames"


def main(run):
    dirs = [d for d in glob.glob(os.path.join(run, "video")) + glob.glob(os.path.join(run, "*", "video")) if os.path.isdir(d)]
    if not dirs:
        return 0
    ffmpeg = find_ffmpeg()
    if ffmpeg is None:
        print("video: ffmpeg not found (install it or set AUTOPLAY_FFMPEG); frames left in", run)
        return 1
    code = 0
    for d in sorted(dirs):
        out, note = make(d, ffmpeg)
        if out:
            print(f"video: {out} ({note})")
        else:
            print(f"video: FAILED for {d}: {note}")
            code = 1
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
