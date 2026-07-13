## Agents sample

Connect to a voice AI agent.

## Tutorial

<img  style="width:1000px;"  alt="Screenshot of the LiveKit Unity SDK Youtube tutorial"  src="https://media.githubusercontent.com/media/livekit/client-sdk-unity/refs/heads/max/final-docs-update-for-2.0.0/.github/youtube_tutorial_screenshot.png">

[Youtube Agents NPC Tutorial](https://www.youtube.com/@livekit_io)

## Getting started

### Project setup

The sample can either be imported via the package manager to get access to the assets or opened as a full Unity project. The project is set up to build and run on all supported platforms.

### Agent dispatch

To talk to an agent, the app needs a token to connect to a LiveKit room and dispatch an agent. The project is already configured to automatically connect to the LiveKit homepage agent you can try at www.livekit.com.

To connect to your own LiveKit server, edit the token source component config with your projects token source.

### Common sample package

In order to get access to common sample functions like the on device scrolling log, make sure to import the [Common](https://github.com/livekit/client-sdk-unity/tree/main/Samples~/Common) sample from the LiveKit Unity Package in the package manager.