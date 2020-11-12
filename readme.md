# HunterPie Sync Plugin
---

This is plugin for [HunterPie](https://github.com/Haato3o/HunterPie) that allows syncing monster buildup data from party-leader to other party members using central server.

> NOTE: this is in relatively early stage, but it in working state.

## Installation

Drag'n'drop this icon below to Plugins screen in HunterPie:

[<img src="https://raw.githubusercontent.com/amadare42/HunterPie.SyncPlugin/master/readme/plugin.svg">](https://raw.githubusercontent.com/amadare42/HunterPie.SyncPlugin/master/Plugin.Sync/bin/Release/module.json)

## Build & Debug

Due to HunterPie's plugin system, it can be a bit tricky to conveniently debug and rebuild project. Here are steps that are required to setup your environment to make it somewhat usable:

1. Checkout this repository as sibling for HunterPie project repository
2. Add Plugin.Sync project as reference for HunterPie solution (optional, but highly recommended)
3. Add following line to post-build event for HunterPie project (including quotes):

    ```"$(MSBuildBinPath)\msbuild.exe" "$(ProjectDir)..\..\Plugin.Sync\Plugin.Sync\Plugin.Sync.csproj"```
4. Set "Run the post-build event" value to "Always", so module binaries will be updated for every build

After these steps, you can just edit plugin project inside HunterPie solution and will have latest binaries for each run so it is easily debbuggable.

Here is diagram:
![project structure](./readme/stucture-scheme.svg)


## Sync server
Server source and communication protocol documentation can be found in it's [repository](https://github.com/amadare42/HunterPie.SyncPlugin.Server).

## Limitations and planned improvements

**Ailment buildup**

HunterPie doesn't allow to easily combine ailment timer from peer client with syncing process, so it is fetched from server instead. This increases data usage and makes UI updates less granular.

**Reflections usage**

In order to alter monster update flow, reflections used heavily. This is making updating process slower and less future-proof.

**Convoluted project structure**

Since plugin must have dependency on HunterPie project, it's impossible to just add this as an dependency for it for straight-forward build and debug. If HunterPie will be able to split plugin dependencies that are shared between application and plugin, required structure can be simplified.  

**Server protocol optimization**

After each monster state update, whole monster data is sent instead of only changed data, plugin sends all monster information at the same time. Protocol and implementation must be updated to be able to handle diff models.

**Websockets support**

Current update scheme using long-polling which isn't ideal, but it simpler to implement than using websockets. Ideally, websockets should be used.