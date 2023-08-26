The GitHub respository for this project is here:
https://github.com/NickThissen/iRacingSdkWrapper.git

In this repostiry, these files have been modified:

iRacingSdkWrapper.csproj
iRSDKSharp.csproj
	<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>

iRacingSDK.cs - Added enums for FFBCommand, ReplaySearchSessionTime, VideoCapture
	public enum BroadcastMessageTypes { CamSwitchPos = 0, CamSwitchNum, CamSetState, ReplaySetPlaySpeed, ReplaySetPlayPosition, ReplaySearch, ReplaySetState, ReloadTextures, ChatCommand, PitCommand, TelemCommand, FFBCommand, ReplaySearchSessionTime, VideoCapture };
