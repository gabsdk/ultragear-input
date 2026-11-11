# Third-party notices

## m1ddc

The macOS display discovery follows the IORegistry walk used by
[m1ddc](https://github.com/waydabber/m1ddc): each `IOMobileFramebuffer` is paired with the
next external `DCPAVServiceProxy`, and displays behind an `AppleDCPMCDP29XX` bridge use I2C chip
address `0xB7`. No source files were copied.

```
MIT License

Copyright (c) 2021 waydabber

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NVIDIA NVAPI and AMD Display Library

The Windows app calls `nvapi64.dll` and `atiadlxx.dll`, which ship with the graphics drivers.
Structure layouts and interface identifiers follow the public headers of the
[NVAPI SDK](https://github.com/NVIDIA/nvapi) and the
[AMD Display Library SDK](https://github.com/GPUOpen-LibrariesAndSDKs/display-library),
both released under the MIT License. No SDK files are redistributed.
