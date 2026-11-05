# Source notes

This repository contains the adapted client component sources and the recovered canopy rain shader sources. Building requires a separately installed copy of Nuclear Option and BepInEx; game assemblies are not included. The Better Night binary in the release is an adapted component and its complete build source is not yet assembled in this repository. NOAtmoFX is a separate optional project and is not redistributed here.

The rain shader and accompanying masks were recovered from Boscali Summer commit c00971bbc9bc976fb5458f5cc7d20dcf0493740d. The serialized CanopyRain and CanopyDroplets shader objects in that commit's weather bundle are byte-identical to the objects in the bundle from d11a507424439f8d84c089afa681bcf86aacb44c used by this release. Third-party notices are included in the client ZIP and under third-party.

The v0.8.1 Git tag and its original client ZIP are the initial release. Current main also contains the experimental five-light approach-row build; see the release's separately named experimental ZIP and source snapshot.
