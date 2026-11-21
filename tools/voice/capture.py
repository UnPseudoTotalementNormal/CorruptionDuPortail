import ctypes as C, time, wave, struct, subprocess, sys, os
here = os.path.dirname(os.path.abspath(__file__))
root = os.path.abspath(os.path.join(here, '..', '..'))
work = os.path.join(os.environ.get('TEMP', here), 'voice-bench'); os.makedirs(work, exist_ok=True)
import shutil
for f in ('steam_api64.dll',): shutil.copy(os.path.join(root, 'Assets/Scripts/Facepunch/Facepunch/redistributable_bin/win64', f), work)
open(os.path.join(work, 'steam_appid.txt'), 'w').write('480')
os.chdir(work)
api = C.CDLL(os.path.join(work, 'steam_api64.dll'))
api.SteamAPI_Init.restype = C.c_bool
if not api.SteamAPI_Init(): sys.exit('SteamAPI_Init failed')
api.SteamAPI_SteamUser_v020.restype = C.c_void_p
u = C.c_void_p(api.SteamAPI_SteamUser_v020())
api.SteamAPI_ISteamUser_GetVoiceOptimalSampleRate.restype = C.c_uint32
print('optimal rate', api.SteamAPI_ISteamUser_GetVoiceOptimalSampleRate(u))
secs = float(sys.argv[1]) if len(sys.argv) > 1 else 12
api.SteamAPI_ISteamUser_StartVoiceRecording(u)
packets = []; pcm = bytearray(); t0 = time.time()
buf = (C.c_uint8 * 16384)(); out = (C.c_uint8 * 262144)()
while time.time() - t0 < secs:
    api.SteamAPI_RunCallbacks()
    n = C.c_uint32(0)
    r = api.SteamAPI_ISteamUser_GetAvailableVoice(u, C.byref(n), None, 0)
    if r == 0 and n.value > 0:
        w = C.c_uint32(0)
        r = api.SteamAPI_ISteamUser_GetVoice(u, True, buf, 16384, C.byref(w), False, None, 0, None, 0)
        if r == 0 and w.value:
            pk = bytes(buf[:w.value]); packets.append(pk)
            ow = C.c_uint32(0)
            r2 = api.SteamAPI_ISteamUser_DecompressVoice(u, buf, w.value, out, 262144, C.byref(ow), 48000)
            if r2 == 0: pcm += bytes(out[:ow.value])
            else: print('decompress fail', r2)
    time.sleep(0.06)
api.SteamAPI_ISteamUser_StopVoiceRecording(u)
print('packets', len(packets), 'bytes', sum(map(len, packets)), 'max', max(map(len, packets)) if packets else 0, 'pcm seconds', len(pcm) / 96000)
with wave.open('steam_decoded.wav', 'wb') as f:
    f.setnchannels(1); f.setsampwidth(2); f.setframerate(48000); f.writeframes(bytes(pcm))
api.SteamAPI_Shutdown()
