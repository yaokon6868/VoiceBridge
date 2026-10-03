# Third-party components

VoiceBridge's own source is MIT-licensed. Dependencies keep their original
licenses and notices. Fish Audio and ChatGPT are separate online services,
not bundled models or services, and their terms still apply.

| Component | Project / license |
|---|---|
| .NET / WPF | https://github.com/dotnet/wpf — MIT |
| ASP.NET Core | https://github.com/dotnet/aspnetcore — MIT |
| NAudio | https://github.com/naudio/NAudio — MIT |
| MessagePack-CSharp | https://github.com/MessagePack-CSharp/MessagePack-CSharp — MIT |
| System.Management / ProtectedData | https://github.com/dotnet/runtime — MIT |

The self-contained release includes the .NET runtime's license and notices
as supplied by `dotnet publish`. Project license texts are bundled in `licenses/`.
The 0.3.0 release does not bundle Python. Optional 0.3.1 candidate AEC packages
include Python (PSF), pywebrtc-audio (Apache-2.0), sounddevice and PortAudio
(MIT), NumPy and its bundled libraries, cffi (MIT), pycparser (BSD), and the
PyInstaller bootloader (GPL with its distribution exception). Exact installed
license texts and dependency notices are copied under app/aec/licenses.
Virtual audio drivers are never bundled.
