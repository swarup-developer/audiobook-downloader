# Third-party notices

Audiobook Downloader is released under the GNU Affero General Public License,
version 3 (see [LICENSE](LICENSE)). The published `AudiobookDownload.exe` also
contains the following components, each under its own licence.

| Component | Used for | Licence |
|-----------|----------|---------|
| [AudibleApi](https://github.com/rmcrackan/AudibleApi) | Signing in to Audible, listing the library, download licences | GNU AGPL 3.0 |
| Dinah.Core | Helper library used by AudibleApi | See the AudibleApi project |
| [AAXClean](https://github.com/Mbucari/AAXClean) | Reading and decrypting AAX and AAXC files | GNU GPL 3.0 |
| [AAXClean.Codecs](https://github.com/Mbucari/AAXClean.Codecs) | MP3 output and splitting by chapter | GNU GPL 3.0 |
| NAudio.Lame and the LAME encoder | MP3 encoding, inside AAXClean.Codecs | GNU LGPL |
| [NAudio.Core](https://github.com/naudio/NAudio) | Audio helpers used by AAXClean.Codecs | MIT |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | JSON used by AudibleApi | MIT |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | The Audible sign-in window | BSD-style licence from Microsoft |
| [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) | Windows notifications when downloads finish | MIT |
| [.NET](https://github.com/dotnet/runtime) and Windows Forms | The runtime the app is built on | MIT |

AudibleApi, AAXClean and AAXClean.Codecs come from the
[Libation](https://github.com/rmcrackan/Libation) family of projects. Many thanks to
their authors.

The full text of each licence is published with the component, at the links above
and in its NuGet package.
