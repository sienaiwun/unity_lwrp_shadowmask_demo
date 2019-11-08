# lwrp_shadowmask_demo
[![license](http://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Tencent/InjectFix/blob/master/LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-blue.svg)](https://github.com/Tencent/InjectFix/pulls)

This demo shows how unity lwrp template works with shadowmask lighting mode. 
![baked shaodow ](https://github.com/sienaiwun/lwrp_shadowmask_demo/blob/master/imgs/shadowmask.png)
As the image shows, the shadow is baked. Even the wall is removed, the shadow remains. Currently, we can support at most 4 baked-shadow lights. As the following image shows.
![multi shaodow ](https://github.com/sienaiwun/lwrp_shadowmask_demo/blob/master/imgs/multi_light.png)

The changes related to shadowmask are mainly in submits [lwrp with shadowmask](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/commit/b1e8c9cc3da5547e16a77df09c9cd8d63494df96) ,[shadowmask fix](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/commit/f08ab82927dedd09df0f8753507a215c403550fe) ,[fix compiler flag error](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/commit/526d9ddf0bf4cffb43a0bb7e81519d5e54d6b71e) and [FIX: shadowmask config explicitly](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/commit/0f26d8d1fd7efae4ce0f8c70c3d7ca232c7edf3e).

Besides the shadowmask, this repo also improves shadowmap's efficiency by tighten its aabb and adds planer specular/glossy reflections.
