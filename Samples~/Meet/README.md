## Meet sample

Connect to a video call with other remote participants.

<img  style="width:1000px;"  alt="Screenshot of the Meet sample"  src="https://media.githubusercontent.com/media/livekit/client-sdk-unity/refs/heads/main/.github/meet_sample_screenshot.png">

## Getting started

### Project setup

The sample can either be imported via the package manager to get access to the assets or opened as a full Unity project. The project is set up to build and run on all supported platforms.

### Token source

In order to connect to your LiveKit server, configure the token source component config used in the scene. To learn more about tokens, see https://docs.livekit.io/frontends/build/authentication.

### Audio system

The LiveKit Unity SDK offers two audio systems. The Unity audio path uses the Unity APIs for audio input and output. Platform Audio is the alternative, where the native LiveKit plugin manages audio input and output. You can select which path to use on the MeetManager component.

### Common sample package

In order to get access to common sample functions like the on device scrolling log, make sure to import the [Common](https://github.com/livekit/client-sdk-unity/tree/main/Samples~/Common) sample from the LiveKit Unity Package in the package manager.