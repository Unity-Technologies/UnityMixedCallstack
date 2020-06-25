Unity Mixed Callstack
=====

UnityMixedCallstack is a Visual Studio 2017/2019 extension to help debug native applications embedding Mono, like Unity.

If you are using Visual Studio 2015 then you will need to grab one of the older releases of UnityMixedCallstack

Mono doesn't generate debug symbols that Visual Studio understands for jitted functions.

As a result, Visual Studio can not show anything meaningful for managed stack frames.

This fork of PmipMyCallstack has been developed for developers at unity-technologies and support has been added to our clone of mono, which can be found here https://github.com/Unity-Technologies/mono

This version requires you to set the UNITY_MIXED_CALLSTACK environment variable before launching visual studio and before launching Unity (or using our mono standalone), which will tell the mono runtime to write out each jit'd function to a file.

The original PmipMyCallstack plugin created by JB Evain would call the function `mono_pmip` on every frame that doesn't belong to a module to show a meaningful representation of the stack frame and display it natively in the call stack window.  This plugin does the same thing, but instead of calling over ipc, mono writes a file with the jit information and we lookup the IP from that. This allows us to open much larger callstacks, and many frames without visual studio hanging for a long time (an issue since Unity has so many threads).

In this version we also display the module the managed code belongs to. This gives you a nice callstack to look at in visual studio.

Remember, you need to disable Just My Code in the debugger options (Debug -> Options...) to expand the `[External Code]` entry in the callstack.

## Before

![Before Unity Mixed Callstack](https://raw.githubusercontent.com/mderoy/UnityMixedCallstack/master/Images/csb.png)

## After

![After Unity Mixed Callstack](https://raw.githubusercontent.com/mderoy/UnityMixedCallstack/master/Images/cs.png)

