# RobertsmaniaPitGirl

This project presents a plugin for Voice Attack that integrates with the iRacing SDK to monitor race events and build a list of event markers to be used for replay review.

The iRacing telemetry data is monitored constantly and markers are recorded when any driver has an offtrack incident, overtake, undertake, or radio broadcast.  The race start and driver finishes are also entered as markers.

The replay marker data can be reviewed and filtered by car and/or marker type.  This makes it easy to go through the replay seeing all the events that were recorded for a specific car, all the events of a particular marker type for any car, or any marker type for any car.

For example:
* "Filter markers by the current car"
Then stepping forward or backward through the next/previous markers will keep the focus on the current car.

* "Filter markers by overtakes"
Then only overtake markers will be presented, and still focus will be on the current car.

* "Clear the marker filters"
Return to the default state of all marker types for all cars will be presented.

The provided profile has commands that can be used with speech, but is also intended to work with a gamepad/controller so the use of speech recognition is not required.

The plugin provides these commands to be used within a Voice Attack profile:
```
RobertsmaniaPitGirlReplay commands:

Print_Info
Print_Cameras
Print_Drivers
Set_Camera | {TXT:~~NewCamera}
Get_Camera | {TXT:~~HoldCamera}!
Watch_MyCar
Watch_MostExciting
Watch_CarNumber | {TXT:~~CarNumber}
Watch_CarPosition | {TXT:~~CarPosition}
Check_CarNumber | {TXT:~~CarNumber}!
Check_CarPosition | {TXT:~~CarPosition} {TXT:~~CarNumber}!
Jump_ToLive
Jump_ToBeginning
Marker_Add
PlayMarker_Next | {TXT:MarkerCarFilter} {TXT:MarkerTypeFilter} {INT:~~ReplayBufferSecs}
                | {TXT:~~MarkerDriver}! {TXT:~~MarkerType}!
PlayMarker_Previous | {TXT:MarkerCarFilter} {TXT:MarkerTypeFilter} {INT:~~ReplayBufferSecs}
                    | {TXT:~~MarkerDriver}! {TXT:~~MarkerType}!
PlayMarker_Last
PlayMarker_First
SeekMarker_First
iRacingIncident_Next
iRacingIncident_Previous
Marker_Count | {INT:~~MarkerCount}
Marker_Summary | {TXT:~~MarkerSummary}! {TXT:~~MostOvertakesCarNum}! 
                 {TXT:~~MostIncidentsCarNum}! {TXT:~~MostBroadcastsCarNum}!
                 {INT:~~IncidentMarkerCount}! {INT:~~OvertakeMarkerCount}!
                 {INT:~~RadioMarkerCount}! {INT:~~ManualMarkerCount}!
                 {INT:~~UndertakeMarkerCount}!
Marker_Summary_CarNumber | {TXT:~~CarNumber} {INT:~~CarNumberMarkerCount}!
                           {INT:~~CarNumberIncidentMarkerCount}! {INT:~~CarNumberOvertakeMarkerCount}!
                           {INT:~~CarNumberRadioMarkerCount}! {INT:~~CarNumberManualMarkerCount}!
                           {INT:~~CarNumberUndertakeMarkerCount}!
```

Installation

You must own a license for Voice Attack to be able to use this (or any other) plugin: https://voiceattack.com

Download the RobertsmaniaPitGirlReplay.vax file from the releases page on this repository.

In the Voice Attack options, disable plugin support if it is currently enabled and restart Voice Attack.

Import the RobertsmaniaPitGirlReplay.vax.  This will install the plugin and sample profile.

Enable plugin support in the Voice Attack options.  Restart Voice Attack.

Select the RobertsmaniaPitGirlReplay profile.  The plugin should be initialized and the commands in the profile are available.
