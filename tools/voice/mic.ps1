param([string]$Action, [string]$Id)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
[Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig { int a(); int b(); int c(); int d(); int e(); int f(); int g(); int h(); int i(); int j();
  [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role); }
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient {}
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int EnumAudioEndpoints(int f, int s, out IntPtr c);
  [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { int Activate(); int OpenPropertyStore(); [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id); }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator {}
public static class Mic {
  public static string Get(int role) { var e = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice d; e.GetDefaultAudioEndpoint(1, role, out d); string id; d.GetId(out id); return id; }
  public static int Set(string id) { var p = (IPolicyConfig)new PolicyConfigClient(); int r=0; for (int role=0; role<3; role++) r |= p.SetDefaultEndpoint(id, role); return r; }
}
"@
if ($Action -eq 'get') { "console=" + [Mic]::Get(0); "comms=" + [Mic]::Get(2) }
elseif ($Action -eq 'set') { [Mic]::Set($Id) }
