# Third-party notices

The project's MIT License covers LLM2PCG's original code and software documentation. It does not replace the licenses of third-party dependencies, tools or media.

## Three.js

The browser renderer depends on Three.js. `Web/package-lock.json` currently pins version `0.185.1`; dependencies are installed separately with npm. Preserve the following notice when redistributing Three.js or bundles containing it.

Source: [Three.js repository](https://github.com/mrdoob/three.js). The following text is from the installed dependency's `LICENSE` file:

```text
The MIT License

Copyright © 2010-2026 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## Unity, Node.js and .NET

Unity Editor and the Unity packages declared in `UnityDemo/Packages/manifest.json` and the LLM2PCG package manifest are obtained separately and remain subject to their respective licenses. The LLM2PCG MIT License does not relicense Unity or its packages.

Node.js and the .NET SDK/runtime are also obtained separately. Retain their applicable notices when redistributing those runtimes or packaging a self-contained application.

## Media and third-party assets

Showcase screenshots have separate terms in [Asset and media license](ASSET_LICENSE.md). The underlying external artwork and development scenes are not distributed here. No external asset license is transferred by displaying an image.

Add notices for any newly incorporated third-party code, dependency or asset before redistributing it. This document records the identified dependencies; it is not a certification of every contributor's ownership or of every possible distribution's compliance.
