# lwrp_shadowmask_demo
[![license](http://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/blob/master/LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-blue.svg)](https://github.com/sienaiwun/unity_lwrp_shadowmask_demo/pulls)

This demo shows how Unity's LWRP or URP template works with shadowmask lighting mode. 
Currently, we can support at most 4 baked-shadow lights. As the following image shows.

![multi shaodow ](https://github.com/sienaiwun/lwrp_shadowmask_demo/blob/master/imgs/multi_light.png)

The shadow information is baked in the shadowmask image. Even the workshop sets on the floor are removed, we can still see the visibility to the directional lights as well as the other two colored point lights. 

![baked shaodow ](https://github.com/sienaiwun/lwrp_shadowmask_demo/blob/master/imgs/shadowmask.png)

## How to merge

For clarity, if you want to integrate these changes into URP, you can also see this [branch](https://github.com/sienaiwun/ScriptableRenderPipeline/commits/universal_shadowmask) which has the main code difference.

