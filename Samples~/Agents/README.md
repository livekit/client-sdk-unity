## Agents sample

Connect to a voice AI agent.

## Tutorial

<img  style="width:1000px;"  alt="Screenshot of the LiveKit Unity SDK Youtube tutorial"  src="https://media.githubusercontent.com/media/livekit/client-sdk-unity/refs/heads/main/.github/youtube_tutorial_screenshot.png">

[Youtube Agents NPC Tutorial](https://www.youtube.com/watch?v=cLreGfOKzN8)

## Getting started

### Project setup

The sample can either be imported via the package manager to get access to the assets or opened as a full Unity project. 

The app is configured to connect to the LiveKit homepage agent by default, which you can also try at [livekit.com](https://www.livekit.com). To point the app at your own agent, see [Connect to your agent](#connect-to-your-agent).

### Connect to your agent

To switch from the default agent to your own, you first need a LiveKit agent to speak with. For a no-code setup, use the [Agent Builder](https://docs.livekit.io/agents/start/builder/). For more customization, try our starter agent for [Python](https://github.com/livekit-examples/agent-starter-python), [Node.js](https://github.com/livekit-examples/agent-starter-node), or [create your own from scratch](https://docs.livekit.io/agents/start/voice-ai/).

Second, you need a token server. For development, the easiest option is the development token server: enable it from your project's Options on the Settings page in LiveKit Cloud and copy the token server ID.

Then create a new TokenSoureComponentConfig asset for your development token server and reference it in the scene on the `TokenSourceComponent` script instead of the `HomepageAgent.asset`:

### Common sample package

In order to get access to common sample functions like the on device scrolling log, make sure to import the [Common](https://github.com/livekit/client-sdk-unity/tree/main/Samples~/Common) sample from the LiveKit Unity Package in the package manager.