/*
Copyright (c) 2023 Robertsmania
All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using iRacingSdkWrapper;
using iRSDKSharp;
using Newtonsoft.Json;

namespace Robertsmania
{

    public sealed class VAPlugin_RobertsmaniaReplay
    {
        private static dynamic _vaProxy;
        private static SdkWrapper _iRSDKWrapper;

        private const float cVersion = 1.002f;
        private const int cUpdatesPerSec = 4;
        public const int cThrottleMSecs = 1000 / cUpdatesPerSec;
        public const int cMaxCars = 64;
        private const int cMaxCameras = 25;
        private const int cWatchMyCarIdx = -4;
        private const int cDontChangeCarIdx = -100;
        private const int cWatchMostExcitingCarIdx = -1;

        private const int cSeekWaitTries = 100;
        private const int cReplaySearchToleranceFrames = 30;
        private const int cMarkerSearchToleranceSecs = 2;
        private const int cReplay2LiveToleranceSecs = 4;

        private const int cStartBufferSecs = 2;
        private const int cRollingStartBufferMult = 3;
        private const int cOvertakeThresholdSecs = 3;
        private const int cUndertakeThresholdSecs = 4;
        private const int cOvertakeBufferSecs = 5;
        private const int cUndertakeBufferSecs = 5;
        private const int cIncidentBufferSecs = 5;
        private const int cRadioBufferSecs = 2;
        private const int cRecapBufferSecs = 4;
        private const int cReplayStartSecs = 45;
        private const int cReplayStartMaxMarkers = 6;
        private const int cTransitionSleepMSecs = 500;

        public const int cIncidentMarkerTimeoutSecs = 5;
        public const int cPositionMarkerTimeoutSecs = 1;
        public const int cRadioMarkerTimeoutSecs = 5;
        public const int cNotInWorldTimeoutSecs = 5;

        public static bool g_Connected = false;
        public static bool g_WatchingLive = false;
        public static bool g_PracticingSession = false;
        public static bool g_QualifyingSession = false;
        public static bool g_RacingSession = false;

        private static string g_EventType = "";
        private static int g_SubSessionID = -1;
        private static int g_NumCarClasses = 0;
        private static int g_StandingStart = 0;
        public static bool g_IsInGarage = false;
        public static bool g_IsOnTrack = false;
        public static bool g_IsOnTrackCar = false;
        public static bool g_IsReplayPlaying = false;
        private static string g_SessionDisplayName = "not yet in a session";
        private static string g_SimMode = "";

        public static iRacingSdkWrapper.Bitfields.SessionFlag g_CurrentSessionFlags;
        public static int g_CamCarIdx = -1;
        public static int g_PlayerCarIdx = -1;
        private static int g_CamGroupNumber;
        private static int g_RadioTransmitCarIdx = -1;
        private static int g_CurrentSessionNum = -1;
        public static int g_CurrentSessionTime = 0;
        public static int g_CurrentSessionTimeRemain = 0;
        private static int g_ReplayFrameNum = 0;
        private static int g_ReplaySessionNum = -1;
        public static int g_ReplaySessionTime = 0;
        private static int g_ReplayPlaySpeed = 0;
        public static int g_PlayerCarDriverIncidentCount = 0;

        private static int g_NumDrivers = 0;
        private static int g_FinalLap = -1;
        private static bool g_LeaderFinished = false;

        private static MarkerType g_MarkerTypeFilter = MarkerType.Wildcard;
        private static int g_MarkerCarIdxFilter = -1;

        public static iRacingSdkWrapper.SessionStates g_SessionState = iRacingSdkWrapper.SessionStates.Invalid;
        public static iRacingSdkWrapper.Bitfields.CameraState g_CamCameraState = new iRacingSdkWrapper.Bitfields.CameraState();

        public static int[] g_CarIdxIncidentMarkerTimes = new int[cMaxCars];
        public static int[] g_CarIdxPositionMarkerTimes = new int[cMaxCars];
        public static int[] g_CarIdxRadioMarkerTimes = new int[cMaxCars];
        public static int[] g_CarIdxNotInWorldTimes = new int[cMaxCars];

        public static int[] g_CarIdxLap = new int[cMaxCars];
        public static float[] g_CarIdxLapDistPct = new float[cMaxCars];
        public static TrackSurfaces[] g_CarIdxTrackSurface = new TrackSurfaces[cMaxCars];
        public static TrackSurfaces[] g_OldCarIdxTrackSurface = new TrackSurfaces[cMaxCars];

        public static List<string> g_SessionNames;
        public static Event g_RaceStartEvent = new Event(MarkerType.Start, 0, 0, cDontChangeCarIdx, 0);
        public static List<Event> g_Markers = new List<Event>();
        public static List<FlagStatus> g_TimeFlagStatus = new List<FlagStatus>();
        public static Dictionary<string, int> g_Cameras = new Dictionary<string, int>();
        public static DriverEntry[] g_Drivers = new DriverEntry[cMaxCars];
        public static TrackPosition[] g_TrackPositions; 
        public static List<TimeLapPosition>[] g_CarIdxTimeLapPositions = new List<TimeLapPosition>[cMaxCars];

        public static HashSet<int> g_CarClasses;


        #region datatypes 
        public enum MarkerType
        {
            Manual = 0,
            Radio = 1,
            Incident = 2,
            Overtake = 3,
            Undertake = 4,
            Start = 5,
            Finish = 6,
            Wildcard = 7
        }

        public enum EventCompareType
        {
            Forward = 0,
            Reverse = 1
        }

        public struct FlagStatus
        {
            public int Session { get; }
            public int Time { get; }
            public iRacingSdkWrapper.Bitfields.SessionFlag Flags;
            public FlagStatus(int session, int time, iRacingSdkWrapper.Bitfields.SessionFlag flags)
            {
                Session = session;
                Time = time;
                Flags = flags;
            }
        }

        public struct TimeLapPosition
        {
            public int Session { get; }
            public int Time { get; }
            public int Lap { get; }
            public int Position { get; }
            public int ClassPosition { get; }
            public TimeLapPosition(int session = 0, int time = 0, int lap = 0, int position = 0, int classPosition = 0)
            {
                Session = session;
                Time = time;
                Lap = lap;
                Position = position;
                ClassPosition = classPosition;
            }

            public static bool Compare(TimeLapPosition tlp1, TimeLapPosition tlp2)
            {
                if (tlp1.Session == tlp2.Session && tlp1.Time < tlp2.Time)
                {
                    return true;
                }
                else if (tlp1.Session < tlp2.Session)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public struct Event
        {
            public int Time { get; }
            public int Session { get; }
            public int CarIdx { get; }
            public int Position { get; }
            public int ClassPosition { get; }
            public int Lap { get; }
            public float DistPct { get; }
            public MarkerType EventType { get; }

            public Event(MarkerType eventType = MarkerType.Manual, int session = 0, int time = 0, int carIdx = 0, int lap =0, int position = 0, int classPosition = 0, float distPct = 0)
            {
                Time = time;
                Session = session;
                CarIdx = carIdx;
                Lap = lap;
                EventType = eventType;
                Position = position;
                ClassPosition = classPosition;
                DistPct = distPct;
            }

            public static bool Compare(Event e1, Event e2, EventCompareType direction)
            {
                //DEBUG check for why no next marker plays
                //_vaProxy.WriteToLog("Event Compare: " + e1.Time + " " + e2.Time);
                if (e1.EventType != MarkerType.Wildcard && e1.EventType != e2.EventType)
                {
                    return false;
                }
                else if (e1.CarIdx != -1 && e1.CarIdx != e2.CarIdx)
                {
                    return false;
                }
                else if (e1.Session == e2.Session && e1.Time < e2.Time && direction == EventCompareType.Forward)
                {
                    return true;
                }
                else if (e1.Session == e2.Session && e1.Time > e2.Time && direction == EventCompareType.Reverse)
                {
                    return true;
                }
                else if (e1.Session < e2.Session && direction == EventCompareType.Forward)
                {
                    return true;
                }
                else if (e1.Session > e2.Session && direction == EventCompareType.Reverse)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool Match(Event e1, Event e2)
            {
                if (e1.Session != e2.Session)
                {
                    return false;
                }
                else if (e1.EventType != MarkerType.Wildcard && e1.EventType != e2.EventType)
                {
                    return false;
                }
                else if (e1.CarIdx != -1 && e1.CarIdx != e2.CarIdx)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            public override string ToString() => $"({Session}:{Time}L:{Lap} C:{CarIdx} P:{Position} CP:{ClassPosition} :{EventType})";
        }

        public struct DriverEntry
        {
            public int CarIdx { get; }
            public int CarNumberRaw { get; }
            public string CarNumStr { get; } 
            public string UserName { get; }
            public int IRating { get; }
            public string License { get; }
            public string CarClassColor { get; }
            public string LicColor { get; }
            public int CarClassId { get; }
            public string CarName { get; }
            public int IsSpectator { get; }

            public DriverEntry(int carIdx = 0, int carNumberRaw = 0, string carNumStr = "0", string userName = " ", int iRating = 0, string license = " ", string carClassColor = " ", string licColor = " ", int carClassId = 0, string carName = " ", int isSpectator = 0)
            {
                CarIdx = carIdx;
                CarNumberRaw = carNumberRaw;
                CarNumStr = carNumStr;
                UserName = userName;
                IRating = iRating;
                License = license; 
                CarClassColor = carClassColor; 
                LicColor = licColor; 
                CarClassId = carClassId;
                CarName = carName;
                IsSpectator = isSpectator;
            }

            public override string ToString() => $"[{CarIdx.ToString().PadLeft(2)}:{CarNumStr.PadLeft(3)}:{CarClassId.ToString().PadLeft(4)}:{UserName}]";
            //public override string ToString() => $"[{CarIdx}:{CarNumberRaw}:{CarNumStr}:{UserName}:{IRating}:{License}:{CarClassColor}:{LicColor}] ";
        }

        public struct TrackPosition
        {
            public DriverEntry Driver { get; }
            public float DistPct { get; set; }
            public int Lap { get; set; }
            public TrackSurfaces TrackSurface { get; set; }
            public int Position { get; set; }
            public int PositionUpFrom { get; set; }
            public int PositionDownFrom { get; set; }
            public int PositionUpTime { get; set; }
            public int PositionDownTime { get; set; }
            public int FinalPosition { get; set; }
            public int ClassPosition { get; set; }
            public bool Finished { get; set; }

            public TrackPosition(DriverEntry driverEntry, float distPct = -1, TrackSurfaces trackSurface = TrackSurfaces.NotInWorld, int lap = -1, int position = -1, int positionUpFrom = -1, int positionDownFrom = -1, int positionUpTime = 0, int positionDownTime = 0)
            {
                Driver = driverEntry;
                DistPct = distPct;
                Lap = lap;
                TrackSurface = trackSurface;
                Position = position;
                ClassPosition = position;
                PositionUpFrom = positionUpFrom;
                PositionDownFrom = positionDownFrom;
                PositionUpTime = positionUpTime;
                PositionDownTime = positionDownTime;
                FinalPosition = 999;
                Finished = false;
            }

            public static bool Compare(TrackPosition tp1, TrackPosition tp2)
            {
                if (tp1.Lap == tp2.Lap && tp1.DistPct < tp2.DistPct)
                {
                    return true;
                }
                else if (tp1.Lap < tp2.Lap)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public override string ToString() => $"(P{Position.ToString().PadLeft(2)}:CP{ClassPosition.ToString().PadLeft(2)}/{PositionUpFrom.ToString().PadLeft(2)}/{PositionDownFrom.ToString().PadLeft(2)}:TU{PositionUpTime.ToString().PadLeft(5)}:TD{PositionDownTime.ToString().PadLeft(5)}:{DistPct.ToString("F3").PadLeft(6)}:L{Lap.ToString().PadLeft(2)}:FP{FinalPosition.ToString().PadLeft(3)}:{TrackSurface.ToString().PadLeft(15)}:{Driver})";
        }
        #endregion

        private static void OnConnected(object sender, System.EventArgs e)
        {
            g_Connected = true;
        }
        
        private static void OnDisconnected(object sender, System.EventArgs e)
        {
            g_Connected = false;
            g_SessionDisplayName = "no longer in a session";

            bool autoLoadSaveReplayFiles = _vaProxy.GetBoolean("AutoLoadSaveReplayFiles") ?? false;
            bool recordMarkersInReplayMode = _vaProxy.GetBoolean("RecordMarkersInReplayMode") ?? false;
            if (autoLoadSaveReplayFiles && g_Markers != null && g_Markers.Any() && (g_SimMode == "full" || recordMarkersInReplayMode))
            {
                SaveMarkers(g_Markers);
            }
        }

        private static void ResetTrackPositionData()
        {
            g_TrackPositions = null;
            g_LeaderFinished = false;
            g_FinalLap = -1;
            g_CarIdxNotInWorldTimes = new int[cMaxCars];
            //There is no before
            for (int i = 0; i < g_Drivers.Length; i++)
            {
                if (g_CarIdxTimeLapPositions[i] != null)
                {
                    g_CarIdxTimeLapPositions[i].Clear();
                }
            }
        }

        private static void OnTelemetryUpdated(object sender, SdkWrapper.TelemetryUpdatedEventArgs e)
        {
            g_PlayerCarIdx = e.TelemetryInfo.PlayerCarIdx.Value;
            g_CamGroupNumber = e.TelemetryInfo.CamGroupNumber.Value;
            g_RadioTransmitCarIdx = _iRSDKWrapper.GetTelemetryValue<int>("RadioTransmitCarIdx").Value;
            g_CurrentSessionTime = (int)e.TelemetryInfo.SessionTime.Value;
            g_CurrentSessionTimeRemain = (int)e.TelemetryInfo.SessionTimeRemain.Value;
            g_ReplayFrameNum = e.TelemetryInfo.ReplayFrameNum.Value;
            g_ReplaySessionNum = e.TelemetryInfo.ReplaySessionNum.Value;
            g_ReplaySessionTime = (int)e.TelemetryInfo.ReplaySessionTime.Value;
            g_ReplayPlaySpeed = e.TelemetryInfo.ReplayPlaySpeed.Value;
            g_IsInGarage = e.TelemetryInfo.IsInGarage.Value;
            g_IsOnTrack = e.TelemetryInfo.IsOnTrack.Value;
            g_IsOnTrackCar = _iRSDKWrapper.GetTelemetryValue<bool>("IsOnTrackCar").Value;
            g_IsReplayPlaying = e.TelemetryInfo.IsReplayPlaying.Value;
            g_CamCameraState = e.TelemetryInfo.CamCameraState.Value;
            g_CamCarIdx = e.TelemetryInfo.CamCarIdx.Value;
            int newPlayerCarDriverIncidentCount = e.TelemetryInfo.PlayerCarDriverIncidentCount.Value;

            //Did the session just transition?
            if (g_CurrentSessionNum != e.TelemetryInfo.SessionNum.Value)
            {
                ResetTrackPositionData();
            }
            g_CurrentSessionNum = e.TelemetryInfo.SessionNum.Value;

            //Do this each update just in case, bad to miss a session transition
            if (g_SessionNames != null && g_CurrentSessionNum >= 0 && g_SessionNames.Count > g_CurrentSessionNum && g_SessionNames.Count >= e.TelemetryInfo.SessionNum.Value)
            {
                string sessionName = g_SessionNames[g_CurrentSessionNum];
                switch (sessionName)
                {
                    case "RACE":
                        {
                            g_RacingSession = true;
                            g_PracticingSession = false;
                            g_QualifyingSession = false;
                            g_SessionDisplayName = "racing";
                            break;
                        }
                    case "PRACTICE":
                        {
                            g_PracticingSession = true;
                            g_QualifyingSession = false;
                            g_RacingSession = false;
                            g_SessionDisplayName = "practicing";
                            break;
                        }
                    case "QUALIFY":
                        {
                            g_QualifyingSession = true;
                            g_PracticingSession = false;
                            g_RacingSession = false;
                            g_SessionDisplayName = "qualifying";
                            break;
                        }
                    case "TESTING":
                        {
                            g_RacingSession = false;
                            g_PracticingSession = false;
                            g_QualifyingSession = false;
                            g_SessionDisplayName = "testing";
                            break;
                        }
                    default: //?
                        {
                            g_RacingSession = false;
                            g_PracticingSession = false;
                            g_QualifyingSession = false;
                            g_SessionDisplayName = "waiting";
                            break;
                        }
                }
            }
            else
            {
                if (g_SubSessionID != -1) //Otherwise waiting for session data
                {
                    _vaProxy.WriteToLog("g_CurrentSessionNum OUT OF BOUNDS: " + g_CurrentSessionNum, "red");
                    if (g_SessionNames != null)
                    {
                        _vaProxy.WriteToLog("g_SessionNames count: " + g_SessionNames.Count, "red");
                    }
                }
            }

            //Make an effort to set positions once everyone is in the car before the start
            if (g_SessionState == SessionStates.GetInCar && 
                e.TelemetryInfo.SessionState.Value != SessionStates.GetInCar &&
                g_TrackPositions != null)
            {
                ResetTrackPositionData();
            }
            g_SessionState = e.TelemetryInfo.SessionState.Value;

            //Monitor telemetry arrays
            g_CarIdxLap = e.TelemetryInfo.CarIdxLap.Value;
            g_CarIdxLapDistPct = e.TelemetryInfo.CarIdxLapDistPct.Value;
            g_CarIdxTrackSurface = e.TelemetryInfo.CarIdxTrackSurface.Value;

            //Is the replay showing live?  Need to check the time remaining for the case when we disconnect from server (goes to a large negative value then)
            g_WatchingLive = Math.Abs(g_CurrentSessionTime - g_ReplaySessionTime) < cReplay2LiveToleranceSecs && g_CurrentSessionTimeRemain > -1000;

            //Monitor Track Positons, Look for Overtakes/Undertakes
            if (g_RacingSession && g_SessionState == SessionStates.Checkered && g_FinalLap == -1)
            {
                g_FinalLap = g_CarIdxLap.Max();
            }
            //Build track position array
            if (g_TrackPositions == null)
            {
                //Only do this if we actually have driver data
                if (g_NumDrivers > 0)
                {
                    g_TrackPositions = new TrackPosition[g_NumDrivers];
                    for (int carIdx = 0; carIdx < g_NumDrivers; carIdx++)
                    {
                        TrackPosition trackPosition = new TrackPosition(g_Drivers[carIdx], g_CarIdxLapDistPct[carIdx], g_CarIdxTrackSurface[carIdx], g_CarIdxLap[carIdx]);
                        g_TrackPositions[carIdx] = trackPosition;
                    }
                }
            }
            //Update track position data
            else
            {
                //Sort by CarIdx to update 
                Array.Sort(g_TrackPositions, (p1, p2) =>
                {
                    return p1.Driver.CarIdx.CompareTo(p2.Driver.CarIdx);
                });

                //Set Lap, DistPct, TrackSurface and FinalPosition
                for (int carIdx = 0; carIdx < g_TrackPositions.Count(); carIdx++)
                {
                    //Keep the Pace Car out of the way
                    if (g_TrackPositions[carIdx].Driver.UserName == "Pace Car")
                    {
                        g_TrackPositions[carIdx].Lap = -2;
                        g_TrackPositions[carIdx].DistPct = -1;
                        continue;
                    }
                    //Starting a new lap?  Add time lap position data for this car at this time
                    if (g_TrackPositions[carIdx].Lap > 0 &&
                        g_TrackPositions[carIdx].Lap < g_CarIdxLap[carIdx])
                    {
                        //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                        TimeLapPosition carTimeLapPosition = new TimeLapPosition(g_CurrentSessionNum, g_CurrentSessionTime, g_CarIdxLap[carIdx], g_TrackPositions[carIdx].Position, g_TrackPositions[carIdx].ClassPosition);
                        g_CarIdxTimeLapPositions[carIdx].Add(carTimeLapPosition);
                    }

                    //Is this the leader finishing? Make really sure.
                    if (g_RacingSession &&
                        !g_LeaderFinished && 
                        g_FinalLap > 0 &&
                        g_SessionState == SessionStates.Checkered &&
                        new[]{TrackSurfaces.OnTrack, TrackSurfaces.OffTrack}.Contains(g_CarIdxTrackSurface[carIdx]) && 
                        g_CarIdxLap[carIdx] > g_FinalLap)
                    {
                        g_LeaderFinished = true;
                        Event finishEvent = new Event(MarkerType.Finish, g_CurrentSessionNum, g_CurrentSessionTime, carIdx, g_TrackPositions[carIdx].Lap, g_TrackPositions[carIdx].Position, g_TrackPositions[carIdx].ClassPosition, g_TrackPositions[carIdx].DistPct);
                        AddMarker(finishEvent);
                    } 
                    //Cars finishing
                    if (g_LeaderFinished &&
                        new[]{TrackSurfaces.OnTrack, TrackSurfaces.OffTrack}.Contains(g_CarIdxTrackSurface[carIdx]) &&
                        g_TrackPositions[carIdx].Lap < g_CarIdxLap[carIdx])
                    {
                        //TODO check for photo finish?  We are really using the postion from the previous update
                        g_TrackPositions[carIdx].Finished = true;
                        g_TrackPositions[carIdx].FinalPosition = g_TrackPositions[carIdx].Position;
                        g_TrackPositions[carIdx].Lap = g_CarIdxLap[carIdx];
                        g_TrackPositions[carIdx].TrackSurface = g_CarIdxTrackSurface[carIdx];
                        //Force DistPct to reflect their FinalPosition so the order is static
                        g_TrackPositions[carIdx].DistPct = (float)(100 - g_TrackPositions[carIdx].FinalPosition) / 100;
                        Event finishEvent = new Event(MarkerType.Finish, g_CurrentSessionNum, g_CurrentSessionTime, carIdx, g_TrackPositions[carIdx].Lap, g_TrackPositions[carIdx].Position, g_TrackPositions[carIdx].ClassPosition, g_TrackPositions[carIdx].DistPct);
                        AddMarker(finishEvent);
                    } 

                    //Otherwise someone who hasnt finished
                    else if (!g_TrackPositions[carIdx].Finished)
                    {
                        g_TrackPositions[carIdx].TrackSurface = g_CarIdxTrackSurface[carIdx];
                        //Keep track of cars blinking out of the world
                        if (g_TrackPositions[carIdx].TrackSurface == TrackSurfaces.NotInWorld)
                        {
                            //_vaProxy.WriteToLog($"Car {carIdx} blinking out. {g_CurrentSessionTime}", "yellow");
                            g_CarIdxNotInWorldTimes[carIdx] = g_CurrentSessionTime;
                        }
                        //Cars in pits dont count, push to bottom of standings
                        if (g_TrackPositions[carIdx].TrackSurface == TrackSurfaces.AproachingPits || 
                            g_TrackPositions[carIdx].TrackSurface == TrackSurfaces.InPitStall)
                        {
                            //TODO handle multiple people on pit row less arbitrarily
                            g_TrackPositions[carIdx].DistPct = -1;
                            g_TrackPositions[carIdx].Lap = g_CarIdxLap[carIdx];
                        }
                        else 
                        {
                            //Sometimes Lap increases and DistPct is still very high so they appear to leapfrog ahead
                            if (g_TrackPositions[carIdx].Lap > 0  &&
                                g_TrackPositions[carIdx].Lap < g_CarIdxLap[carIdx] && 
                                g_CarIdxLapDistPct[carIdx] > 0.5)
                            {
                                //DEBUG look at the Lap transition DistPct
                                //_vaProxy.WriteToLog(g_TrackPositions[carIdx].Driver.UserName + " started lap " + g_CarIdxLap[carIdx] + " with DistPct " + g_CarIdxLapDistPct[carIdx]);
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                                //_iRSDKWrapper.Camera.SwitchToCar(g_TrackPositions[carIdx].Driver.CarNumberRaw);
                                g_TrackPositions[carIdx].DistPct = 0;
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                            }
                            else
                            {
                                g_TrackPositions[carIdx].DistPct = g_CarIdxLapDistPct[carIdx];
                            }
                            
                            //Hack for race start when everyone is on Lap 1 in g_CarIdxLap but before the start/finish
                            if (new[]{SessionStates.Racing, SessionStates.Checkered}.Contains(g_SessionState) &&
                                (g_CarIdxLap[carIdx] >= 1 || g_TrackPositions[carIdx].DistPct < 0.25))
                            {
                                g_TrackPositions[carIdx].Lap = g_CarIdxLap[carIdx];
                            }
                        }
                    }
                }

                //Computers are fast
                Array.Sort(g_TrackPositions, (p1, p2) =>
                {
                    //Lap first then distance
                    int ret = p2.Lap.CompareTo(p1.Lap);
                    return ret != 0 ? ret : p2.DistPct.CompareTo(p1.DistPct);
                });

                //Do initialization and undertakes first so we can validate overtakes
                for (int p = 0; p < g_TrackPositions.Count(); p++)
                {
                    //Dont change cars that have finished
                    if (g_TrackPositions[p].Finished)
                    {
                        continue;
                    }
                    else if (g_TrackPositions[p].Position == -1) //New here?
                    {
                        g_TrackPositions[p].Position = p + 1;
                        g_TrackPositions[p].PositionUpFrom = -1;
                        g_TrackPositions[p].PositionDownFrom = -1;
                        g_TrackPositions[p].PositionUpTime = 0;
                        g_TrackPositions[p].PositionDownTime = 0;
                    }
                    //Racing track surface and not blinking out?
                    else if (g_TrackPositions[p].DistPct >= 0 &&
                        TrackSurfacesCheck(g_TrackPositions[p].TrackSurface) &&
                        g_TrackPositions[p].Position < p + 1) //undertake
                    {
                        //DEBUG watch the overtaken
                        //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                        //_iRSDKWrapper.Camera.SwitchToCar(g_TrackPositions[p].Driver.CarNumberRaw);
                        //_vaProxy.WriteToLog($"Undertake Detected {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");

                        //Set time in second pass below if verified
                        //g_TrackPositions[p].PositionDownTime = g_CurrentSessionTime;
                        g_TrackPositions[p].PositionDownFrom = g_TrackPositions[p].Position;

                        //_iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                        g_TrackPositions[p].Position = p + 1;
                    }
                }
                //Now look for overtakes
                for (int p = 0; p < g_TrackPositions.Count(); p++)
                {
                    //Dont change cars that have finished
                    if (g_TrackPositions[p].Finished)
                    { 
                        continue;
                    }
                    else if (g_TrackPositions[p].DistPct >= 0 &&
                        TrackSurfacesCheck(g_TrackPositions[p].TrackSurface) &&
                        g_TrackPositions[p].Position > p + 1) //overtake 
                    {
                        //_vaProxy.WriteToLog($"Overtake Suspected {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                        //Already in an overtake? Only set timer and old position if not
                        if (g_TrackPositions[p].PositionUpTime == 0)
                        {
                            //Check for an undertake PositionDownFrom that matches the overtake position
                            //If there is one, then consider it valid - othewhise pit/tow/disconnect/freebie
                            TrackPosition[] lostPositions = Array.FindAll(g_TrackPositions, t => t.PositionDownFrom == p + 1);
                            if (lostPositions.Any())
                            {
                                //TODO handle multiple lost position results?!?
                                TrackPosition lostPosition = lostPositions.First();
                                //_vaProxy.WriteToLog($"LostPosition {lostPosition.Driver.CarIdx} {lostPosition.TrackSurface.ToString()} {lostPositions.Count()} {g_CurrentSessionTime}");
                                //Racing track surface, lost car blinking out, this car blinking out?
                                if (TrackSurfacesCheck(lostPosition.TrackSurface) && 
                                    NotInWorldPositionCheck(lostPosition.Driver.CarIdx) && 
                                    NotInWorldPositionCheck(g_TrackPositions[p].Driver.CarIdx))
                                {
                                    //DEBUG watch the overtake
                                    //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                                    //_iRSDKWrapper.Camera.SwitchToCar(g_TrackPositions[p].Driver.CarNumberRaw);
                                    //_vaProxy.WriteToLog($"Overtake Confirmed {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                                    g_TrackPositions[p].PositionUpTime = g_CurrentSessionTime;
                                    g_TrackPositions[p].PositionUpFrom = g_TrackPositions[p].Position;
                                    //_iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                                }
                            }
                            else
                            {
                                //_vaProxy.WriteToLog($"Overtake Cancelled {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                            }
                        }
                        g_TrackPositions[p].Position = p + 1;
                    }
                }
                //Second pass to verify undertakes
                for (int p = 0; p < g_TrackPositions.Count(); p++)
                {
                    //Dont change cars that have finished
                    if (g_TrackPositions[p].Finished)
                    { 
                        continue;
                    }
                    //Check underpasses set in first pass
                    else if (g_TrackPositions[p].PositionDownFrom != -1 &&
                            g_TrackPositions[p].PositionDownTime == 0)
                    {
                        //_vaProxy.WriteToLog($"Undertake verifying {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                        //Check for an overtake PositionUpFrom that matches the undertake position
                        //If there is one, then consider it valid - othewhise pit/tow/disconnect/freebie
                        TrackPosition[] gainedPositions = Array.FindAll(g_TrackPositions, t => t.PositionUpFrom == p + 1);
                        if (gainedPositions.Any())
                        {
                            TrackPosition gainedPosition = gainedPositions.First();
                            //_vaProxy.WriteToLog($"LostPosition {gainedPosition.Driver.CarIdx} {gainedPosition.TrackSurface.ToString()} {gainedPositions.Count()} {g_CurrentSessionTime}");
                            //Racing track surface and the other driver not blinking out?
                            if (TrackSurfacesCheck(gainedPosition.TrackSurface) && 
                                NotInWorldPositionCheck(gainedPosition.Driver.CarIdx))
                            {
                                g_TrackPositions[p].PositionDownTime = g_CurrentSessionTime;
                                //_vaProxy.WriteToLog($"Undertake Confirmed {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                                //g_TrackPositions[p].PositionDownFrom = g_TrackPositions[p].Position;
                            }
                        }
                        else
                        {
                            //_vaProxy.WriteToLog("Undertake Cancelled - phantom/freebie? " + g_TrackPositions[p]);
                            g_TrackPositions[p].PositionDownFrom = -1;
                            //_vaProxy.WriteToLog($"Undertake Cancelled {g_TrackPositions[p].Driver.CarIdx} p:{g_TrackPositions[p].Position} {g_CurrentSessionTime}");
                        }
                    }
                    else 
                    {
                        //Update position of everyone not involved in a pass
                        g_TrackPositions[p].Position = p + 1;
                    }
                }
                
                //Look for Markers to set
                for (int p = 0; p < g_TrackPositions.Count(); p++)
                {
                    //Keeping an overtake position?
                    //Enough time passed since the overtake was noticed?
                    if (g_TrackPositions[p].PositionUpTime > 0 && 
                        g_CurrentSessionTime-g_TrackPositions[p].PositionUpTime > cOvertakeThresholdSecs)
                    {
                        //Kept or increased position
                        if (g_TrackPositions[p].Position < g_TrackPositions[p].PositionUpFrom) 
                        { 
                            //In the race, past the start, not too soon since last marker for this car?
                            if (new[]{SessionStates.Racing, SessionStates.Checkered}.Contains(g_SessionState) &&
                                g_RacingSession &&
                                g_TrackPositions[p].Lap > 0 && 
                                PositionMarkerTimeOk(g_TrackPositions[p].Driver.CarIdx) &&
                                g_TrackPositions[p].DistPct >= 0)
                            {
                                //DEBUG watch the overtake
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                                //_iRSDKWrapper.Camera.SwitchToCar(g_TrackPositions[p].Driver.CarNumberRaw);
                                //_vaProxy.WriteToLog("Overtake Detected");
                                Event overtakeEvent = new Event(MarkerType.Overtake, g_CurrentSessionNum, g_TrackPositions[p].PositionUpTime - cOvertakeBufferSecs, g_TrackPositions[p].Driver.CarIdx, g_TrackPositions[p].Lap, g_TrackPositions[p].Position, g_TrackPositions[p].ClassPosition, g_TrackPositions[p].DistPct);
                                AddMarker(overtakeEvent);
                                //_vaProxy.WriteToLog("Overtake detected.  Position: " + g_TrackPositions[p].Position + " CarIdx: " + g_TrackPositions[p].Driver.CarIdx + " Marker: " + overtakeEvent);
                                g_CarIdxPositionMarkerTimes[g_TrackPositions[p].Driver.CarIdx] = g_CurrentSessionTime;
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                            }
                        }
                        //Reset in any case
                        g_TrackPositions[p].PositionUpTime = 0;
                        g_TrackPositions[p].PositionUpFrom = -1;
                    }
                    else if (g_TrackPositions[p].PositionUpTime > g_CurrentSessionTime)
                    {
                        //Shouldnt happen live, but can if we are testing in replay
                        g_TrackPositions[p].PositionUpTime = 0;
                    }

                    //Keeping an undertake position?
                    //Enough time passed since the undertake was noticed?
                    if (g_TrackPositions[p].PositionDownTime > 0 && 
                        g_CurrentSessionTime-g_TrackPositions[p].PositionDownTime > cUndertakeThresholdSecs)
                    {
                        //Lost or decreased position
                        if (g_TrackPositions[p].Position > g_TrackPositions[p].PositionDownFrom) 
                        { 
                            //In the race, past the start, not too soon since last marker for this car?
                            if (new[]{SessionStates.Racing, SessionStates.Checkered}.Contains(g_SessionState) &&
                                g_RacingSession &&
                                g_TrackPositions[p].Lap > 0 && 
                                PositionMarkerTimeOk(g_TrackPositions[p].Driver.CarIdx) &&
                                g_TrackPositions[p].DistPct >= 0)
                            {
                                Event undertakeEvent = new Event(MarkerType.Undertake, g_CurrentSessionNum, g_TrackPositions[p].PositionDownTime - cUndertakeBufferSecs, g_TrackPositions[p].Driver.CarIdx, g_TrackPositions[p].Lap, g_TrackPositions[p].Position, g_TrackPositions[p].ClassPosition, g_TrackPositions[p].DistPct);
                                AddMarker(undertakeEvent);
                                //_vaProxy.WriteToLog("Undertake detected.  Position: " + g_TrackPositions[p].Position + " CarIdx: " + g_TrackPositions[p].Driver.CarIdx + " Marker: " + undertakeEvent);
                                g_CarIdxPositionMarkerTimes[g_TrackPositions[p].Driver.CarIdx] = g_CurrentSessionTime;
                                //DEBUG watch the overtaken
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(0);
                                //_iRSDKWrapper.Camera.SwitchToCar(g_TrackPositions[p].Driver.CarNumberRaw);
                                //_vaProxy.WriteToLog("Undertake Completed");
                                //_iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                            }
                        }
                        //Reset in any case
                        g_TrackPositions[p].PositionDownTime = 0;
                        g_TrackPositions[p].PositionDownFrom = -1;
                    }
                    else if (g_TrackPositions[p].PositionDownTime > g_CurrentSessionTime)
                    {
                        //Shouldnt happen live, but can if we are testing in replay
                        g_TrackPositions[p].PositionDownTime = 0;
                    }
                }

                //Set ClassPositions 
                Array.Sort(g_TrackPositions, (p1, p2) =>
                {
                    //Lap CarClassId then Position
                    int ret = p1.Driver.CarClassId.CompareTo(p2.Driver.CarClassId);
                    return ret != 0 ? ret : p1.Position.CompareTo(p2.Position);
                });
                //Iterate through g_ClassPositions and set in sequence
                foreach (var carClass in g_CarClasses)
                {
                    int cp = 1;
                    for (int i = 0; i < g_TrackPositions.Length; i++)
                    {
                        if (g_TrackPositions[i].Driver.CarClassId == carClass)
                        {
                            //Set a TimeLapPosition if class position changed
                            if (g_TrackPositions[i].ClassPosition != cp)
                            {
                                TimeLapPosition carTimeLapPosition = new TimeLapPosition(g_CurrentSessionNum, g_CurrentSessionTime, g_TrackPositions[i].Lap, g_TrackPositions[i].Position, cp);
                                g_CarIdxTimeLapPositions[g_TrackPositions[i].Driver.CarIdx].Add(carTimeLapPosition);
                            }
                            g_TrackPositions[i].ClassPosition = cp++;
                        }
                    }
                }

                //Just for display at this point 
                Array.Sort(g_TrackPositions, (p1, p2) =>
                {
                    //Lap first then distance
                    int ret = p2.Lap.CompareTo(p1.Lap);
                    return ret != 0 ? ret : p2.DistPct.CompareTo(p1.DistPct);
                });

            }

            //Did the flags just change?
            if (g_CurrentSessionFlags == null || g_CurrentSessionFlags.ToString() != e.TelemetryInfo.SessionFlags.Value.ToString())
            {
                FlagStatus currentFlagStatus = new FlagStatus(g_CurrentSessionNum, g_CurrentSessionTime, e.TelemetryInfo.SessionFlags.Value);
                g_TimeFlagStatus.Add(currentFlagStatus);
            }
            g_CurrentSessionFlags = e.TelemetryInfo.SessionFlags.Value;

            //Look for Race Start - Standing on StartReady, Rolling on StartGo
            if (g_RaceStartEvent.Time == 0 && 
                (g_StandingStart == 1 && g_CurrentSessionFlags.Contains(iRacingSdkWrapper.Bitfields.SessionFlags.StartReady) ||
                 g_StandingStart == 0 && g_CurrentSessionFlags.Contains(iRacingSdkWrapper.Bitfields.SessionFlags.StartGo)))
            {
                g_RaceStartEvent = new Event(MarkerType.Start, g_CurrentSessionNum, g_CurrentSessionTime, cDontChangeCarIdx, 1);
                AddMarker(g_RaceStartEvent);
                //_vaProxy.WriteToLog("Race Start Detected: " + g_RaceStartEvent);
            }

            //Look for Radio Chatter
            //We'll still see radio broadcast events watching in the past, so only record them when watching live
            if (g_WatchingLive && g_RadioTransmitCarIdx > -1 && g_RadioTransmitCarIdx < cMaxCars && RadioMarkerTimeOk(g_RadioTransmitCarIdx))
            {
                int position = 0;
                int classPosition = 0;
                float distPct = 0;
                if (g_TrackPositions != null)
                {
                    position = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_RadioTransmitCarIdx).Position;
                    classPosition = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_RadioTransmitCarIdx).ClassPosition;
                    distPct = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_RadioTransmitCarIdx).DistPct;
                }
                Event radioEvent = new Event(MarkerType.Radio, g_CurrentSessionNum, g_CurrentSessionTime - cRadioBufferSecs, g_RadioTransmitCarIdx, g_CarIdxLap[g_RadioTransmitCarIdx], position, classPosition, distPct);
                AddMarker(radioEvent);
                //_vaProxy.WriteToLog("Radio Chatter Detected: " + radioEvent);
                g_CarIdxRadioMarkerTimes[g_RadioTransmitCarIdx] = g_CurrentSessionTime;
            }

            //Look for Incidents
            //Check for iRacing incidents - only availble for our car.
            //Always add regardless of other incdident marker times or session type.
            if (newPlayerCarDriverIncidentCount >= 1 && newPlayerCarDriverIncidentCount > g_PlayerCarDriverIncidentCount)
            {
                int position = 0;
                int classPosition = 0;
                float distPct = 0;
                if (g_TrackPositions != null)
                {
                    position = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_PlayerCarIdx).Position;
                    classPosition = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_PlayerCarIdx).ClassPosition;
                    distPct = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_PlayerCarIdx).DistPct;
                }
                Event incidentEvent = new Event(MarkerType.Incident, g_CurrentSessionNum, g_CurrentSessionTime - cIncidentBufferSecs, g_PlayerCarIdx, g_CarIdxLap[g_PlayerCarIdx], position, classPosition, distPct);
                AddMarker(incidentEvent);
                //_vaProxy.WriteToLog("iRacing Incident Detected: " + incidentEvent);
                g_CarIdxIncidentMarkerTimes[g_PlayerCarIdx] = g_CurrentSessionTime;

                g_PlayerCarDriverIncidentCount = newPlayerCarDriverIncidentCount;
            }

            //Look for Offtrack Incidents for everyone
            if (g_RacingSession || g_PracticingSession)
            {
                for (int carIdx = 0; carIdx < g_CarIdxTrackSurface.Count(); carIdx++)
                {
                    if (g_CarIdxTrackSurface[carIdx] == TrackSurfaces.OffTrack && g_OldCarIdxTrackSurface[carIdx] == TrackSurfaces.OnTrack)
                    {
                        if (IncidentMarkerTimeOk(carIdx))
                        {
                            int position = 0;
                            int classPosition = 0;
                            float distPct = 0;
                            if (g_TrackPositions != null)
                            {
                                position = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == carIdx).Position;
                                classPosition = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == carIdx).ClassPosition;
                                distPct = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == carIdx).DistPct;
                            }
                            Event incidentEvent = new Event(MarkerType.Incident, g_CurrentSessionNum, g_CurrentSessionTime - cIncidentBufferSecs, carIdx, g_CarIdxLap[carIdx], position, classPosition, distPct);
                            AddMarker(incidentEvent);
                            //_vaProxy.WriteToLog("OffTrack Incident Detected: " + incidentEvent);
                            g_CarIdxIncidentMarkerTimes[carIdx] = g_CurrentSessionTime;
                        } 
                    }
                }
                g_OldCarIdxTrackSurface = g_CarIdxTrackSurface;
            }
        }

        private static void OnSessionInfoUpdated(object sender, SdkWrapper.SessionInfoUpdatedEventArgs e)
        {
            //_vaProxy.WriteToLog("Session Info Updated");
            if (e.SessionInfo["WeekendInfo"]["EventType"].TryGetValue(out string eventTypeStr))
            {
                g_EventType = eventTypeStr;
            }

            //Always reset driver table when session info changes, people may have joined
            //TODO find how many Drivers in the YAML list so we dont trigger silent exceptions going off the end
            //TODO at least check to see if there is a DriverInfo section?
            g_CarClasses = new HashSet<int>();
            g_NumDrivers = 0;
            for (int i = 0; i < cMaxCars; i++)
            {
                int carIdx = -1;
                int carNumberRaw = -1;
                string carNumStr = "-1";
                string userName = " ";
                int iRating = -1;
                string license = " ";
                string carClassColor = " ";
                string licColor = " ";
                int carClassId = -1;
                string carName = " ";
                int isSpectator = 0;

                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarIdx"].TryGetValue(out string carIdxStr))
                {
                    if (carIdxStr != "")
                    {
                        carIdx = Convert.ToInt32(carIdxStr);
                    }
                }
                //There can be weird entries in practice sessions,
                //spectator is at the end in races,
                //keep going for the whole table but stop counting for races
                if (carIdx == -1 && g_EventType == "Race")
                {
                    continue;
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarNumberRaw"].TryGetValue(out string carNumberRawStr))
                {
                    if (carNumberRawStr != "")
                    {
                        carNumberRaw = Convert.ToInt32(carNumberRawStr);
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarNumber"].TryGetValue(out string carNumberStr))
                {
                    if (carNumberStr != "")
                    {
                        carNumStr = carNumberStr;
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["UserName"].TryGetValue(out string userNameStr))
                {
                    if (userNameStr != "")
                    {
                        userName = userNameStr;
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["IRating"].TryGetValue(out string iRatingStr))
                {
                    if (iRatingStr != "")
                    {
                        iRating = Convert.ToInt32(iRatingStr);
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["LicString"].TryGetValue(out string licenseStr))
                {
                    if (licenseStr != "")
                    {
                        license = licenseStr;
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarClassColor"].TryGetValue(out string carClassColorStr))
                {
                    if (carClassColorStr != "")
                    {
                        carClassColor = "#" + Convert.ToInt32(carClassColorStr,16).ToString("X6");
                        //int carClassColorInt= Convert.ToInt32(carClassColorStr,16);
                        //CarClassColor = carClassColorInt.ToString("X6");
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["LicColor"].TryGetValue(out string licColorStr))
                {
                    if (licColorStr != "")
                    {
                        licColor = "#" + Convert.ToInt32(licColorStr, 16).ToString("X6");
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarClassID"].TryGetValue(out string carClassIdStr))
                {
                    if (carClassIdStr != "")
                    {
                        carClassId = Convert.ToInt32(carClassIdStr);
                        g_CarClasses.Add(carClassId);
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["CarScreenName"].TryGetValue(out string carNameStr))
                {
                    if (carNameStr != "")
                    {
                        carName = carNameStr;
                    }
                }
                if (e.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i]["IsSpectator"].TryGetValue(out string isSpectatorStr))
                {
                    if (isSpectatorStr != "")
                    {
                        isSpectator = Convert.ToInt32(isSpectatorStr);
                    }
                }
                g_Drivers[g_NumDrivers] = new DriverEntry(carIdx, carNumberRaw, carNumStr, userName, iRating, license, carClassColor, licColor, carClassId, carName, isSpectator);
                g_NumDrivers++;
            }

            int subSessionID = 0;
            if (e.SessionInfo["WeekendInfo"]["SubSessionID"].TryGetValue(out string subSessionIDStr))
            {
                if (subSessionIDStr != null)
                {
                    subSessionID = Convert.ToInt32(subSessionIDStr);
                }
            }

            if (subSessionID != g_SubSessionID)
            {
                //New Session - reset everything
                g_SubSessionID = subSessionID;
                g_MarkerTypeFilter = MarkerType.Wildcard;
                _vaProxy.SetText("MarkerTypeFilter","Wildcard");
                g_MarkerCarIdxFilter = -1;
                _vaProxy.SetText("MarkerCarFilter","-1");
                g_TrackPositions = null;
                g_CarIdxIncidentMarkerTimes = new int[cMaxCars];
                g_CarIdxPositionMarkerTimes = new int[cMaxCars];
                g_CarIdxRadioMarkerTimes = new int[cMaxCars];
                g_CarIdxNotInWorldTimes = new int[cMaxCars];
                g_TimeFlagStatus.Clear();
                g_RaceStartEvent = new Event(MarkerType.Start, 0, 0, cDontChangeCarIdx, 0);
                g_StandingStart = 0;
                g_FinalLap = -1;
                g_LeaderFinished = false;
                g_PlayerCarDriverIncidentCount = 0; 
                g_CarIdxTimeLapPositions = new List<TimeLapPosition>[cMaxCars];
                g_SimMode = "";
                g_Markers = new List<Event>();

                bool autoLoadSaveReplayFiles = _vaProxy.GetBoolean("AutoLoadSaveReplayFiles") ?? false;
                if (autoLoadSaveReplayFiles)
                { 
                    LoadMarkers();
                }

                for (int i = 0; i < cMaxCars; i++)
                {
                    g_CarIdxTimeLapPositions[i] = new List<TimeLapPosition>();
                }

                g_SessionNames = new List<string>();
                int s = 0;
                while (e.SessionInfo["SessionInfo"]["Sessions"]["SessionNum", s]["SessionName"].TryGetValue(out string sessionNameStr))
                {
                    g_SessionNames.Add(sessionNameStr);
                    s++;
                }

                if (e.SessionInfo["WeekendInfo"]["SimMode"].TryGetValue(out string simModeStr))
                {
                    if (simModeStr != null)
                    {
                        g_SimMode =  simModeStr;
                    }
                }

                if (e.SessionInfo["WeekendInfo"]["NumCarClasses"].TryGetValue(out string numCarClassesStr))
                {
                    if (numCarClassesStr != null)
                    {
                        g_NumCarClasses = Convert.ToInt32(numCarClassesStr);
                    }
                }

                if (e.SessionInfo["WeekendInfo"]["WeekendOptions"]["StandingStart"].TryGetValue(out string standingStartStr))
                {
                    if (standingStartStr != null)
                    {
                        g_StandingStart = Convert.ToInt32(standingStartStr);
                    }
                }

                //Cameras change track to track but should be the same for the session
                g_Cameras.Clear();

                int c = 1;
                while (e.SessionInfo["CameraInfo"]["Groups"]["GroupNum", c]["GroupName"].TryGetValue(out string cameraGroupNameStr))
                {
                    if (cameraGroupNameStr != null)
                    {
                        //Avoid case issues with camera names
                        cameraGroupNameStr = cameraGroupNameStr.ToUpper();
                        if (!g_Cameras.ContainsKey(cameraGroupNameStr))
                        {
                            g_Cameras.Add(cameraGroupNameStr, c);
                        }
                    }
                    c++;
                }

                //Seems clumsy, but handle tracks that dont have "TV Static" and/or "TV Mixed"
                if (!g_Cameras.ContainsKey("TV STATIC"))
                {
                    if (g_Cameras.TryGetValue("TV3", out int iTV3))
                    {
                        g_Cameras.Add("TV STATIC", iTV3);
                    }
                }
                if (!g_Cameras.ContainsKey("TV MIXED"))
                {
                    if (g_Cameras.TryGetValue("TV3", out int iTV3))
                    {
                        g_Cameras.Add("TV MIXED", iTV3);
                    }
                }

                if (g_Cameras.Count == 0)
                {
                    _vaProxy.WriteToLog("NO CAMERAS in session update!", "red");
                }
                PrintInfo();
                _vaProxy.WriteToLog("New Session");
            }
        }

        public static bool IncidentMarkerTimeOk(int carIdx)
        {
            if (carIdx < 0)
            {
                return false;
            }
            return ((g_CurrentSessionTime - g_CarIdxIncidentMarkerTimes[carIdx]) > cIncidentMarkerTimeoutSecs);
        }

        public static bool TrackSurfacesCheck(TrackSurfaces trackSurface)
        {
            return new[] { TrackSurfaces.OnTrack, TrackSurfaces.OffTrack }.Contains(trackSurface);
        }

        public static bool NotInWorldPositionCheck(int carIdx)
        {
            return g_CurrentSessionTime - g_CarIdxNotInWorldTimes[carIdx] > cNotInWorldTimeoutSecs;
        }

        public static bool PositionMarkerTimeOk(int carIdx)
        {
            if (carIdx < 0)
            {
                return false;
            }
            //If they have just been blinking/NotInWorld, dont set position change markers.
            //This was checked when overtake/undertakes are detected, but doing it again just to be sure.
            if (!NotInWorldPositionCheck(carIdx))
            {
                //_vaProxy.WriteToLog($"Car {carIdx} blinking out prevented position marker. {g_CurrentSessionTime}", "yellow");
                return false;
            }
            //_vaProxy.WriteToLog($"Position Time Check Car {carIdx} {g_CurrentSessionTime} {g_CarIdxPositionMarkerTimes[carIdx]}", "yellow");
            return ((g_CurrentSessionTime - g_CarIdxPositionMarkerTimes[carIdx]) > cPositionMarkerTimeoutSecs);
        }

        public static bool RadioMarkerTimeOk(int carIdx)
        {
            if (carIdx < 0)
            {
                return false;
            }
            return ((g_CurrentSessionTime - g_CarIdxRadioMarkerTimes[carIdx]) > cRadioMarkerTimeoutSecs);
        }

        private static void SeekWait()
        {
            //While seeking the replay frame num stays mostly the same until completed, then it jumps
            int tries = cSeekWaitTries;
            bool searching = true;

            if (g_Connected)
            {
                int oldReplayFrameNum = g_ReplayFrameNum;
                while (searching && tries > 0)
                {
                    int newReplayFrameNum = g_ReplayFrameNum;
                    if (Math.Abs(newReplayFrameNum - oldReplayFrameNum) > cReplaySearchToleranceFrames)
                    {
                        searching = false;
                    }
                    else
                    {
                        tries--;
                        Thread.Sleep(2 * cThrottleMSecs);  //kind of arbitrary, but wait 2 data update cycle times
                    }
                }
            }
            Thread.Sleep(cThrottleMSecs); //Just a little extra to be sure we dont step on something...
        }

        private static void PrintInfo()
        {
            string infoStr = g_SubSessionID + " S:" + g_ReplaySessionNum + " T:" + g_ReplaySessionTime + " F:" + g_ReplayFrameNum + " " + g_CurrentSessionFlags + " ";
            infoStr += (g_WatchingLive) ? "LIVE" : "Replaying";
            infoStr += $" SimMode: {g_SimMode}";
            string markersStr = "Markers:" + g_Markers.Count + "\n";
            string flagsStr = "Flags:" + g_TimeFlagStatus.Count + "\n";
            int i = 0;
            foreach (Event Event in g_Markers)
            {
                markersStr += Event + " ";
                i++;
                if (i % 5 == 0) markersStr += "\n";
                if (i > 20)
                {
                    markersStr += Event + " [too many more to show]";
                    break;
                }
            }
            _vaProxy.WriteToLog(markersStr);
            //_vaProxy.WriteToLog("Race Start Event: " + g_RaceStartEvent);
            //_vaProxy.WriteToLog(flagsStr);
            _vaProxy.WriteToLog(infoStr);
        }

        private static void PrintCameras()
        {
            string cameraStr = "";
            int count = 1;
            foreach (KeyValuePair<string, int> camera in g_Cameras)
            {
                cameraStr += "[" + (camera.Value.ToString() + ":" + camera.Key + "] ");
                if (count % 9 == 0) cameraStr += "\n";
                count++;
            }
            _vaProxy.WriteToLog(cameraStr);
            _vaProxy.WriteToLog("Print cameras: " + g_Cameras.Count(), "purple");
        }

        private static void PrintDrivers()
        {
            string driverStr = "";
            for (int i = 0; i<g_Drivers.Count(); i++)
            {
                driverStr += g_Drivers[i];
                if (i > 0 && i % 10 == 0) driverStr += "\n";
            }
            _vaProxy.WriteToLog(driverStr);
            _vaProxy.WriteToLog("Print drivers: " + g_Drivers.Count(), "purple");
        }

        private static string DriverNames()
        {
            string driverNames = "";
            if (g_Connected)
            {
                for (int i = 0; i < g_Drivers.Count(); i++)
                {
                    if (g_Drivers[i].UserName != "Pace Car")
                    { 
                        driverNames += " " + g_Drivers[i].UserName;
                    }
                }
            }
            return driverNames;
        }

        private static void AddMarker(Event newEvent)
        {
            //Exit if the sim isnt actually running (saved replay file)
            //and the option for record markers in replay mode is false.
            bool recordMarkersInReplayMode = _vaProxy.GetBoolean("RecordMarkersInReplayMode") ?? false;
            if (g_SimMode != "full" && !recordMarkersInReplayMode) 
            {
                return;
            }

            if (!g_Markers.Contains(newEvent))
            {
                g_Markers.Add(newEvent);
                _vaProxy.WriteToLog($"Marker added: {newEvent}", "green");
                if (g_SimMode != "full")
                {
                    _vaProxy.WriteToLog("Adding markers in Replay Mode. Toggle Record Markers In Replay Mode to disable.", "yellow");
                }
            }
            //computers are fast
            g_Markers.Sort((e1, e2) =>
            {
                //session first then time
                int ret = e1.Session.CompareTo(e2.Session);
                return ret != 0 ? ret : e1.Time.CompareTo(e2.Time);
            });

            //DEBUG - Change camera to the new marker CarIdx
            //if (g_WatchingLive && newEvent.CarIdx > -1)
            //{
                //_vaProxy.SetText("~~CarNumber",g_Drivers[newEvent.CarIdx].CarNumStr);
                //_vaProxy.Command.Execute("A0 RobertsmaniaReplay - Watch Car Number");
            //}    
        }

        private static void SaveMarkers(List<Event> markers)
        {
            // Get the "My Documents" folder path
            string myDocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Create a new folder named "RobertsmaniaReplays" if it doesn't exist
            string outputFolderPath = Path.Combine(myDocumentsPath, "RobertsmaniaReplays");
            Directory.CreateDirectory(outputFolderPath);

            if (g_SubSessionID == -1)
            {
                _vaProxy.WriteToLog("Subsession ID not set. Cannot save markers.", "red");
                return;
            }

            string fileName = $"markers_{g_SubSessionID}.json";

            try
            {
                string json = JsonConvert.SerializeObject(markers);
                File.WriteAllText(Path.Combine(outputFolderPath, fileName), json);
                _vaProxy.WriteToLog("Markers saved successfully.", "green");
            }
            catch (Exception ex)
            {
                _vaProxy.WriteToLog($"Error saving markers: {ex.Message}", "red");
            }
        }

        public static void LoadMarkers()
        {
            if (g_SubSessionID == -1)
            {
                _vaProxy.WriteToLog("Subsession ID not set. Cannot load markers.", "yellow");
                return;
            }

            // Get the "My Documents" folder path
            string myDocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Reference the folder named "PitGirlReplays" 
            string inputFolderPath = Path.Combine(myDocumentsPath, "PitGirlReplays");

            string fileName = $"markers_{g_SubSessionID}.json";

            if (!File.Exists(Path.Combine(inputFolderPath, fileName)))
            {
                _vaProxy.WriteToLog("No marker data found", "red");
                return;
            }

            try
            {
                string json = File.ReadAllText(Path.Combine(inputFolderPath, fileName));
                var loadedMarkers = JsonConvert.DeserializeObject<List<Event>>(json);

                if (loadedMarkers != null)
                {
                    g_Markers = loadedMarkers;
                    Event start = g_Markers.FirstOrDefault(e => e.EventType == MarkerType.Start);

                    if (start.EventType == MarkerType.Start)
                    {
                        g_RaceStartEvent = start;
                    }

                    //computers are fast
                    g_Markers.Sort((e1, e2) =>
                    {
                        //session first then time
                        int ret = e1.Session.CompareTo(e2.Session);
                        return ret != 0 ? ret : e1.Time.CompareTo(e2.Time);
                    });

                    _vaProxy.WriteToLog("Markers loaded successfully.", "green");
                }
            }
            catch (Exception ex)
            {
                _vaProxy.WriteToLog($"Error loading markers: {ex.Message}", "red");
            }
        }

        private static void PlayEvent(Event stEvent, int bufferSecs = 0)
        {
            //Switch the camera to the car, if we have one
            if (stEvent.CarIdx >= 0)
            { //CarIdx:0 is the pace car in races, but a driver entry in practices
                _iRSDKWrapper.Camera.SwitchToCar(g_Drivers[stEvent.CarIdx].CarNumberRaw);
            }

            //Search to the replay time in the marker with optional bufferSecs
            _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearchSessionTime, stEvent.Session, (stEvent.Time - bufferSecs) * 1000);

            //Was having hickups on some transitions that seemed like timing issues, now always seek wait
            SeekWait();

            //Always come out of this playing
            if (g_ReplayPlaySpeed != 1)
            {
                _iRSDKWrapper.Replay.SetPlaybackSpeed(1);
            }
        }
        private static int CheckCarNumber(string checkCarNumberStr)
        {
            if (g_Connected && g_Drivers != null && checkCarNumberStr != null)
            {
                //Return the CarNumberRaw for the given checkCarNumberStr
                return (g_Drivers.FirstOrDefault(driver => driver.CarNumStr == checkCarNumberStr).CarNumberRaw);
            }
            else
            {
                return -1;
            }
        }

        private static int CheckCarPosition(string checkCarPositionStr)
        {
            if (g_Connected && g_Drivers != null && checkCarPositionStr != null)
            {
                int checkCarPosition = Convert.ToInt32(checkCarPositionStr);
                //Return the CarNumberRaw for the given checkCarPositionStr using track position
                //TODO class positions?
                return (g_TrackPositions.FirstOrDefault(p => p.Position == checkCarPosition).Driver.CarNumberRaw);
            }
            else
            {
                return -1;
            }
        }

        private static void CheckMarkerCarFilter()
        {
            string markerCarFilterStr = _vaProxy.GetText("MarkerCarFilter");
            if (markerCarFilterStr != null)
            {
                int markerCarFilterRaw = CheckCarNumber(markerCarFilterStr); 
                int markerCarFilterInt = Convert.ToInt32(markerCarFilterStr);
                if (markerCarFilterInt == cWatchMyCarIdx)
                {
                    g_MarkerCarIdxFilter = g_PlayerCarIdx;
                }
                else if (markerCarFilterInt == cDontChangeCarIdx)
                {
                    g_MarkerCarIdxFilter = g_CamCarIdx;
                }
                else if (markerCarFilterRaw > 0)
                {
                    g_MarkerCarIdxFilter = g_Drivers.FirstOrDefault(driver => driver.CarNumberRaw == markerCarFilterRaw).CarIdx;
                    //If not found, defualt will be 0 so set to -1 to indicate we couldnt find it
                    if (g_MarkerCarIdxFilter == 0)
                    {
                        g_MarkerCarIdxFilter = -1;
                    }
                }
                else
                {
                    g_MarkerCarIdxFilter = -1;
                }
            }
            else
            {
                g_MarkerCarIdxFilter = -1;
            }
        }

        private static void CheckMarkerTypeFilter()
        {
            string markerTypeFilterStr = _vaProxy.GetText("MarkerTypeFilter");
            if (markerTypeFilterStr != null)
            {
                switch (markerTypeFilterStr)
                {
                    case "Overtake":
                        g_MarkerTypeFilter = MarkerType.Overtake;
                        break;
                    case "Undertake":
                        g_MarkerTypeFilter = MarkerType.Undertake;
                        break;
                    case "Incident":
                        g_MarkerTypeFilter = MarkerType.Incident;
                        break;
                    case "Radio":
                        g_MarkerTypeFilter = MarkerType.Radio;
                        break;
                    case "Manual":
                        g_MarkerTypeFilter = MarkerType.Manual;
                        break;
                    case "Wildcard":
                        g_MarkerTypeFilter = MarkerType.Wildcard;
                        break;
                    default:
                        g_MarkerTypeFilter = MarkerType.Wildcard;
                        break;
                }
            }
            else
            {
                g_MarkerTypeFilter = MarkerType.Wildcard;
            }
        }

        #region PluginBoilerplate
        public static string VA_DisplayName()
        {
            return "RobertsmaniaReplay_VAPlugin - v" + cVersion.ToString();
        }

        public static string VA_DisplayInfo()
        {
            return "PitGIrl iRacing Replay VoiceAttack Plugin.\r\n\r\n2023 Robertsmania";
        }

        public static Guid VA_Id()
        {
            return new Guid("{BEDD99AF-141F-47DC-9C4F-1CFDB6FD6E13}");
        }

        //Should I really be doing something with this in my routines?  
        //static Boolean _stopVariableToMonitor = false;

        //this function is called from VoiceAttack when the 'stop all commands' button is pressed or a, 'stop all commands' action is called.  this will help you know if processing needs to stop if you have long-running code
        public static void VA_StopCommand()
        {
            //_stopVariableToMonitor = true;
        }

        public static void VA_Exit1(dynamic vaProxy)
        {
            //this function gets called when VoiceAttack is closing (normally).  You would put your cleanup code in here, but be aware that your code must be robust enough to not absolutely depend on this function being called
            //TODO any other objects that need to be removed or stopped?

            if (_iRSDKWrapper != null)
            {
                _iRSDKWrapper.Stop();
                _iRSDKWrapper = null;
            }

            bool autoLoadSaveReplayFiles = _vaProxy.GetBoolean("AutoLoadSaveReplayFiles") ?? false;
            if (autoLoadSaveReplayFiles &&  g_Markers != null && g_Markers.Any())
            {
                SaveMarkers(g_Markers);
            }
        }

        public static void VA_Init1(dynamic vaProxy)
        {
            //this is where you can set up whatever session information that you want.  this will only be called once on voiceattack load, and it is called asynchronously.
            //the SessionState property is a local copy of the state held on to by VoiceAttack.  In this case, the state will be a dictionary with zero items.  You can add as many items to hold on to as you want.
            //note that in this version, you can get and set the VoiceAttack variables directly.

            _vaProxy = vaProxy;
            _vaProxy.Command.Execute("Initialize RobertsmaniaReplay plugin", true, true, null);

            if (_iRSDKWrapper == null)
            {
                _iRSDKWrapper = new SdkWrapper();
            }
            _iRSDKWrapper.TelemetryUpdated += OnTelemetryUpdated;
            _iRSDKWrapper.SessionInfoUpdated += OnSessionInfoUpdated;
            _iRSDKWrapper.Connected += OnConnected;
            _iRSDKWrapper.Disconnected += OnDisconnected;

            _iRSDKWrapper.TelemetryUpdateFrequency = cUpdatesPerSec;
            _iRSDKWrapper.Start();

            //seems clumsy but there is no default constror for structs
            //TODO any other data structures need intialiaztion?
            for (int i = 0; i < g_Drivers.Length; i++)
            {
                g_Drivers[i] = new DriverEntry(0, 0);
            }
        }
        #endregion

        public static void ShowUsage()
        {
            string usage = "RobertsmaniaReplay commands:\n";
            usage += "Print_Info\n";
            usage += "Print_Cameras\n";
            usage += "Print_Drivers\n";
            usage += "Set_Camera | {TXT:~~NewCamera}\n";
            usage += "Get_Camera | {TXT:~~HoldCamera}!\n";
            usage += "Watch_MyCar\n";
            usage += "Watch_MostExciting\n";
            usage += "Watch_CarNumber | {TXT:~~CarNumber}\n";
            usage += "Watch_CarPosition | {TXT:~~CarPosition}\n";
            usage += "Check_CarNumber | {TXT:~~CarNumber}!\n";
            usage += "Check_CarPosition | {TXT:~~CarPosition} {TXT:~~CarNumber}!\n";
            usage += "Jump_ToLive\n";
            usage += "Jump_ToBeginning\n";
            usage += "Marker_Add\n";
            usage += "PlayMarker_Next | {TXT:MarkerCarFilter} {TXT:MarkerTypeFilter} {INT:~~ReplayBufferSecs}\n";
            usage += "                | {TXT:~~MarkerDriver}! {TXT:~~MarkerType}!\n";
            usage += "PlayMarker_Previous | {TXT:MarkerCarFilter} {TXT:MarkerTypeFilter} {INT:~~ReplayBufferSecs}\n";
            usage += "                | {TXT:~~MarkerDriver}! {TXT:~~MarkerType}!\n";
            usage += "PlayMarker_Last\n";
            usage += "PlayMarker_First\n";
            usage += "SeekMarker_First\n";
            usage += "iRacingIncident_Next\n";
            usage += "iRacingIncident_Previous\n";
            usage += "Marker_Count | {INT:~~MarkerCount}\n";
            usage += "Marker_Summary | {TXT:~~MarkerSummary}! {TXT:~~MostOvertakesCarNum}!\n"; 
            usage += "                 {TXT:~~MostIncidentsCarNum}! {TXT:~~MostBroadcastsCarNum}!\n";
            usage += "                 {INT:~~IncidentMarkerCount}! {INT:~~OvertakeMarkerCount}!\n";
            usage += "                 {INT:~~RadioMarkerCount}! {INT:~~ManualMarkerCount}!\n";
            usage += "                 {INT:~~UndertakeMarkerCount}!\n";
            usage += "Marker_Summary_CarNumber | {TXT:~~CarNumber} {INT:~~CarNumberMarkerCount}!\n";
            usage += "                           {INT:~~CarNumberIncidentMarkerCount}! {INT:~~CarNumberOvertakeMarkerCount}!\n";
            usage += "                           {INT:~~CarNumberRadioMarkerCount}! {INT:~~CarNumberManualMarkerCount}!\n";
            usage += "                           {INT:~~CarNumberUndertakeMarkerCount}!\n";
            usage += "Clear_Markers\n";
            usage += "Load_Markers | Documents/RobertsmaniaReplays/markers_SessionID.json\n";
            usage += "Save_Markers | Documents/RobertsmaniaReplays/markers_SessionID.json\n";
            _vaProxy.WriteToLog(usage, "pink");
        }

        public static void VA_Invoke1(dynamic vaProxy)
        {
            #region notes
            //vaProxy.Context - a string that can be anything you want it to be.  this is passed in from the command action.  this was added to allow you to just pass a value into the plugin in a simple fashion (without having to set conditions/text values beforehand).  Convert the string to whatever type you need to.
            //vaProxy.SessionState - all values from the state maintained by VoiceAttack for this plugin.  the state allows you to maintain kind of a, 'session' within VoiceAttack.  this value is not persisted to disk and will be erased on restart. other plugins do not have access to this state (private to the plugin)
            //the SessionState dictionary is the complete state.  you can manipulate it however you want, the whole thing will be copied back and replace what VoiceAttack is holding on to

            //the following get and set the various types of variables in VoiceAttack.  note that any of these are nullable (can be null and can be set to null).  in previous versions of this interface, these were represented by a series of dictionaries
            //vaProxy.SetSmallInt and vaProxy.GetSmallInt - use to access short integer values (used to be called, 'conditions')
            //vaProxy.SetText and vaProxy.GetText - access text variable values
            //vaProxy.SetInt and vaProxy.GetInt - access integer variable values
            //vaProxy.SetDecimal and vaProxy.GetDecimal - access decimal variable values
            //vaProxy.SetBoolean and vaProxy.GetBoolean - access boolean variable values
            //vaProxy.SetDate and vaProxy.GetDate - access date/time variable values

            //to indicate to VoiceAttack that you would like a variable removed, simply set it to null.  all variables set here can be used withing VoiceAttack.
            //note that the variables are global (for now) and can be accessed by anyone, so be mindful of that while naming
            #endregion

            switch (vaProxy.Context)
            {
                case "Test_Command":
                    {
                        vaProxy.SetText("~~SaySomething", "Greetings!");
                        vaProxy.Command.Execute("A0 - SpeechCoordinator - Wait Say Something", false, true, null, "\"Greetings.\"");
                        break;
                    }

                case "Clear_Markers":
                    {
                        g_Markers = new List<Event>();

                        break;
                    }

                case "Save_Markers":
                    {
                        SaveMarkers(g_Markers);

                        break;
                    }

                case "Load_Markers":
                    {
                        LoadMarkers();
                        
                        break;
                    }
                
                case "Replay_RaceStart":
                    {
                        int raceStartBufferSecs = (g_StandingStart == 0) ?
                            cStartBufferSecs * cRollingStartBufferMult:
                            cStartBufferSecs;

                        CheckMarkerCarFilter();
                        if (g_MarkerCarIdxFilter != -1)
                        {
                            _iRSDKWrapper.Camera.SwitchToCar(g_Drivers[g_MarkerCarIdxFilter].CarNumberRaw);
                        }
                        //Try to handle the case where we havent seen the race start
                        if (g_RaceStartEvent.Time == 0)
                        {
                            //start of current session, then next lap than back a few secs 
                            Event stEvent = new Event(MarkerType.Start, g_CurrentSessionNum, g_ReplaySessionTime, cDontChangeCarIdx, 1);
                            _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearchSessionTime, g_CurrentSessionNum, 0);
                            SeekWait();
                            _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearch, (int)ReplaySearchModeTypes.NextLap, 0);
                            SeekWait();
                            _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearchSessionTime, g_CurrentSessionNum, (g_ReplaySessionTime - raceStartBufferSecs) * 1000);
                            //vaProxy.WriteToLog("No race start observed, doing our best to fake it", "pink");
                            g_RaceStartEvent = new Event(MarkerType.Start, g_ReplaySessionNum, g_ReplaySessionTime, cDontChangeCarIdx, 1);
                            AddMarker(g_RaceStartEvent);
                        }
                        else
                        {
                            PlayEvent(g_RaceStartEvent, raceStartBufferSecs);
                            //vaProxy.WriteToLog("Play Race Start at: " + g_RaceStartEvent, "purple");
                        }
                        SeekWait();
                        
                        _iRSDKWrapper.Replay.SetPlaybackSpeed(1);

                        break;
                    }

                case "Set_Camera":
                    {
                        //Sets the iRacing camera to {TXT:~~NewCamera} if its valid
                        string newCameraStr = vaProxy.GetText("~~NewCamera");
                        if (newCameraStr == null)
                        {
                            vaProxy.WriteToLog("Couldnt set camera, no {TXT:~~NewCamera} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        newCameraStr = newCameraStr.ToUpper();
                        if (!g_Cameras.TryGetValue(newCameraStr, out int newCameraGroup))
                        {
                            PrintCameras();
                            vaProxy.WriteToLog("Couldnt set camera, no camera group : " + newCameraStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        _iRSDKWrapper.Camera.SwitchToCar(cDontChangeCarIdx, newCameraGroup);
                        //vaProxy.WriteToLog("Camera set to " + newCameraStr + ":" + newCameraGroup, "purple"); 
                        break; 
                    } 

                case "Get_Camera":
                    {
                        //Gets the current camera group name and writes it to {TXT:~~HoldCamera}
                        string currentCameraStr = g_Cameras.FirstOrDefault(x => x.Value == g_CamGroupNumber).Key;
                        if (currentCameraStr == null)
                        {
                            //Should really never happen, it would mean we have a runtime camera group number thats not in the session camera list
                            vaProxy.WriteToLog("Couldnt get camera for CamCameraNumber: " + g_CamGroupNumber + " (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        vaProxy.SetText("~~HoldCamera", currentCameraStr);
                        //vaProxy.WriteToLog("Current CamCameraNumber: " + g_CamGroupNumber + " : " + currentCameraStr, "purple");
                        break;
                    }

                case "Check_CarNumber":
                    {
                        //Checks if the {TXT:~~CarNumber} is in the session, sets to -1 if not found
                        string checkCarNumberStr = vaProxy.GetText("~~CarNumber");
                        if (checkCarNumberStr == null)
                        {
                            vaProxy.SetText("~~CarNumber", "-1");
                            vaProxy.WriteToLog("Couldnt check car number, no {TXT:~~CarNumber} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        int checkCarNumberRaw = CheckCarNumber(checkCarNumberStr);
                        if (checkCarNumberRaw < 1)
                        {
                            vaProxy.SetText("~~CarNumber", "-1");
                            PrintDrivers();
                            vaProxy.WriteToLog("Couldnt find car: " + checkCarNumberStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        //vaProxy.WriteToLog("Found car number: " + checkCarNumberRaw, "purple");
                        break;
                    }

                case "Check_CarPosition":
                    {
                        //Checks if the {TXT:~~CarPosition} is in the session,
                        //sets {TXT:~~CarNumber} to that CarNumberStr or -1 if not found
                        string checkCarPositionStr = vaProxy.GetText("~~CarPosition");
                        if (checkCarPositionStr == null)
                        {
                            vaProxy.SetText("~~CarPosition", "-1");
                            vaProxy.WriteToLog("Couldnt check car number, no {TXT:~~CarPosition} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        int checkCarNumberRaw = CheckCarPosition(checkCarPositionStr);
                        if (checkCarNumberRaw < 1)
                        {
                            vaProxy.SetText("~~CarNumber", "-1");
                            PrintDrivers();
                            vaProxy.WriteToLog("Couldnt find car for position: " + checkCarPositionStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        string checkCarNumStr = g_Drivers.FirstOrDefault(d => d.CarNumberRaw == checkCarNumberRaw).CarNumStr;
                        vaProxy.SetText("~~CarNumber", checkCarNumStr.ToString());
                        //vaProxy.WriteToLog("Found car number: " + checkCarNumStr + " for position: " + checkCarPositionStr, "purple");
                        break;
                    }

                case "Watch_CarNumber":
                    {
                        //Changes the camera focus to {TXT:~~CarNumber}
                        //TODO consider setting {TXT:~~CarNumber} to -1 if its not in the session?
                        string watchCarNumberStr = vaProxy.GetText("~~CarNumber");
                        if (watchCarNumberStr == null)
                        {
                            vaProxy.WriteToLog("Couldnt watch car, no {TXT:~~CarNumber} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        int watchCarNumberRaw = CheckCarNumber(watchCarNumberStr);
                        if (watchCarNumberRaw < 0)
                        {
                            vaProxy.WriteToLog("Couldnt watch car: " + watchCarNumberStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        _iRSDKWrapper.Camera.SwitchToCar(watchCarNumberRaw, 0);
                        //vaProxy.WriteToLog("Watching car number " + watchCarNumberRaw, "purple");
                        break;
                    }

                case "Watch_CarPosition":
                    {
                        //Changes the camera focus to {TXT:~~CarPosition}
                        string watchCarPositionStr = vaProxy.GetText("~~CarPosition");
                        if (watchCarPositionStr == null)
                        {
                            vaProxy.WriteToLog("Couldnt watch car, no {TXT:~~CarPosition} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        int watchCarNumberRaw = CheckCarPosition(watchCarPositionStr);
                        if (watchCarNumberRaw < 0)
                        {
                            vaProxy.WriteToLog("Couldnt watch position: " + watchCarPositionStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        _iRSDKWrapper.Camera.SwitchToCar(watchCarNumberRaw, 0);
                        //vaProxy.WriteToLog("Watching car number " + watchCarNumberRaw, "purple");
                        break;
                    }

                case "Marker_Add":
                    {
                        if (g_Connected)
                        {
                            Event newEvent;
                            int position = 0;
                            int classPosition = 0;
                            float distPct = 0;

                            if (g_TrackPositions != null)
                            {
                                position = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_CamCarIdx).Position;
                                classPosition = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_CamCarIdx).ClassPosition;
                                distPct = g_TrackPositions.FirstOrDefault(tp => tp.Driver.CarIdx == g_CamCarIdx).DistPct;
                            }

                            if (g_WatchingLive)
                            {
                                newEvent = new Event(MarkerType.Manual, g_CurrentSessionNum, g_CurrentSessionTime, g_CamCarIdx, g_CarIdxLap[g_CamCarIdx], position, classPosition, distPct);
                            }
                            else
                            {
                                newEvent = new Event(MarkerType.Manual, g_ReplaySessionNum, g_ReplaySessionTime, g_CamCarIdx);
                            }
                            AddMarker(newEvent);
                            //vaProxy.WriteToLog("New marker: " + newEvent);
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                case "PlayMarker_Next":
                    {
                        if (!g_Markers.Any() || !g_Connected)
                        {
                            //vaProxy.WriteToLog("No markers");
                            break;
                        }

                        int? bufferSecs = vaProxy.GetInt("~~ReplayBufferSecs");
                        if (bufferSecs == null)
                        {
                            bufferSecs = 0;
                        }

                        int compareTime = g_ReplaySessionTime + cMarkerSearchToleranceSecs;
                        int compareSessionNum = g_ReplaySessionNum;
                        bool played = false;
                        CheckMarkerTypeFilter();
                        CheckMarkerCarFilter();
                        Event compareEvent = new Event(g_MarkerTypeFilter, compareSessionNum, compareTime, g_MarkerCarIdxFilter, -1);
                        for (int i = 0; i < g_Markers.Count; i++)
                        {
                            //vaProxy.WriteToLog("Play next marker. FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter + " Time Now:" + compareEvent + " Considering:" + i + " " + g_Markers[i]);
                            if (Event.Compare(compareEvent, g_Markers[i], EventCompareType.Forward))
                            {
                                PlayEvent(g_Markers[i], (int)bufferSecs);
                                played = true;
                                //vaProxy.WriteToLog("Marker played. " + g_Markers[i]);
                                vaProxy.SetText("~~MarkerType", g_Markers[i].EventType.ToString());
                                if (g_Markers[i].CarIdx >= 0 && g_Drivers != null)
                                {
                                    vaProxy.SetText("~~MarkerDriver", g_Drivers[g_Markers[i].CarIdx].UserName);
                                }
                                break;
                            }
                        }
                        if (!played)
                        {
                            vaProxy.WriteToLog("No next marker found.  FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter, "orange");
                        }
                        break;
                    }
            
                case "PlayMarker_Previous":
                    { 
                        if (!g_Markers.Any() || !g_Connected)
                        {
                            //vaProxy.WriteToLog("No markers");
                            break;
                        }

                        int? bufferSecs = vaProxy.GetInt("~~ReplayBufferSecs");
                        if (bufferSecs == null)
                        {
                            bufferSecs = 0;
                        }

                        int compareTime = g_ReplaySessionTime - cMarkerSearchToleranceSecs;
                        int compareSessionNum = g_ReplaySessionNum;
                        bool played = false;
                        CheckMarkerTypeFilter();
                        CheckMarkerCarFilter();
                        Event compareEvent = new Event(g_MarkerTypeFilter, compareSessionNum, compareTime, g_MarkerCarIdxFilter, -1);
                        for (int i = g_Markers.Count - 1; i >= 0; i--)
                        {
                            //vaProxy.WriteToLog("Play previous marker. FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter + " Time Now:" + compareEvent + " Considering:" + i + " " + g_Markers[i]);
                            //if (Event.Compare(g_Markers[i], compareEvent))
                            if (Event.Compare(compareEvent, g_Markers[i], EventCompareType.Reverse))
                            {
                                PlayEvent(g_Markers[i], (int)bufferSecs);
                                played = true;
                                //vaProxy.WriteToLog("Marker played. " + g_Markers[i]);
                                vaProxy.SetText("~~MarkerType", g_Markers[i].EventType.ToString());
                                if (g_Markers[i].CarIdx > 0)
                                {
                                    vaProxy.SetText("~~MarkerDriver", g_Drivers[g_Markers[i].CarIdx].UserName);
                                }
                                break;
                            }
                        }
                        if (!played)
                        {
                            vaProxy.WriteToLog("No previous marker found.  FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter, "orange");
                        }
                        break;
                    }

                case "PlayMarker_Last":
                    {
                        if (!g_Markers.Any() || !g_Connected)
                        {
                            //vaProxy.WriteToLog("No markers");
                            break;
                        }

                        var stEvent = g_Markers.Last();
                        PlayEvent(stEvent);
                        //vaProxy.WriteToLog("Play last marker - " + stEvent);
                        break;
                    }

                case "PlayMarker_First":
                    {
                        if (!g_Markers.Any() || !g_Connected)
                        {
                            //vaProxy.WriteToLog("No markers");
                            break;
                        }

                        var stEvent = g_Markers.First();
                        PlayEvent(stEvent);
                        //vaProxy.WriteToLog("Play first marker - " + stEvent);
                        break;
                    }

                case "SeekMarker_First":
                    {
                        if (!g_Markers.Any() || !g_Connected)
                        {
                            //vaProxy.WriteToLog("No markers");
                            break;
                        }
                        //Start just before the first marker, but use the filters
                        var stEvent = g_Markers.First();
                        int compareTime = stEvent.Time - (cMarkerSearchToleranceSecs * 2);
                        int compareSessionNum = stEvent.Session;
                        bool played = false;
                        CheckMarkerTypeFilter();
                        CheckMarkerCarFilter();
                        Event compareEvent = new Event(g_MarkerTypeFilter, compareSessionNum, compareTime, g_MarkerCarIdxFilter, -1);
                        for (int i = 0; i < g_Markers.Count; i++)
                        {
                            //vaProxy.WriteToLog("Play next marker. FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter + " Time Now:" + compareEvent + " Considering:" + i + " " + g_Markers[i]);
                            if (Event.Compare(compareEvent, g_Markers[i], EventCompareType.Forward))
                            {
                                PlayEvent(g_Markers[i]);
                                played = true;
                                //vaProxy.WriteToLog("Marker played. " + g_Markers[i]);
                                break;
                            }
                        }
                        if (!played)
                        {
                            //vaProxy.WriteToLog("No next marker found.  FilterType: " + g_MarkerTypeFilter + " FilterCarIdx: " + g_MarkerCarIdxFilter);
                        }
                        break; 
                    }

                case "Marker_Count":
                    {
                        //Write the number of markers to {INT:~~MarkerCount)
                        int markerCount = 0;
                        if (g_Connected)
                        {
                            markerCount = g_Markers.Count();
                        }
                        vaProxy.SetInt("~~MarkerCount", markerCount);
                        //vaProxy.WriteToLog("Marker Count:  " + markerCount, "purple");
                        break;
                    }

                case "Marker_Summary":
                    {
                        //Write a summary of the current markers to {TXT:~~MarkerSummary}
                        //Most overtakes in {TXT:~~MostOvertakesCarNum}
                        //Most incidents in {TXT:~~MostIncidentsCarNum}
                        //Most broadcasts in {TXT:~~MostBroadcastsCarNum}
                        //Marker count in {TXT:~~MarkerCount}
                        string summaryStr = "";

                        if (g_Markers.Any() && g_Connected)
                        {
                            int[] overtakes = new int[cMaxCars];
                            int[] undertakes = new int[cMaxCars];
                            int[] incidents = new int[cMaxCars];
                            int[] broadcasts = new int[cMaxCars];
                            int[] manuals = new int[cMaxCars];
                            int mostOvertakes = -1;
                            int overtakeCarIdx = -1;
                            string overtakeCarNumStr = "-1";
                            int mostUndertakes = -1;
                            int undertakeCarIdx = -1;
                            string undertakeCarNumStr = "-1";
                            int mostIncidents = -1;
                            int incidentCarIdx = -1;
                            string incidentCarNumStr = "-1";
                            int mostBroadcasts = -1;
                            int broadcastCarIdx = -1;
                            string broadcastCarNumStr = "-1";
                            int mostManuals = -1;
                            int manualCarIdx = -1;
                            string manualCarNumStr = "-1";
                            int starts = 0;
                            int sessionMarkers = 0;

                            foreach (Event marker in g_Markers)
                            {
                                if (marker.Session == g_CurrentSessionNum)
                                {
                                    switch (marker.EventType)
                                    {
                                        case MarkerType.Overtake:
                                            sessionMarkers++;
                                            overtakes[marker.CarIdx]++;
                                            break;

                                        case MarkerType.Undertake:
                                            sessionMarkers++;
                                            undertakes[marker.CarIdx]++;
                                            break;

                                        case MarkerType.Incident:
                                            sessionMarkers++;
                                            incidents[marker.CarIdx]++;
                                            break;

                                        case MarkerType.Radio:
                                            sessionMarkers++;
                                            broadcasts[marker.CarIdx]++;
                                            break;

                                        case MarkerType.Manual:
                                            sessionMarkers++;
                                            manuals[marker.CarIdx]++;
                                            break;

                                        case MarkerType.Start:
                                            sessionMarkers++;
                                            starts++;
                                            break;
                                    }
                                }
                            }

                            mostOvertakes = overtakes.Max();
                            if (mostOvertakes > 0)
                            {
                                overtakeCarIdx = overtakes.ToList().IndexOf(mostOvertakes);
                                overtakeCarNumStr = g_Drivers[overtakeCarIdx].CarNumStr;
                                vaProxy.SetText("~~MostOvertakesCarNum", overtakeCarNumStr);
                                vaProxy.SetInt("~~OvertakeMarkerCount", overtakes.Sum());
                            }
                            else
                            {
                                vaProxy.SetText("~~MostOvertakesCarNum", "-1");
                                vaProxy.SetInt("~~OvertakeMarkerCount", 0);
                            }

                            mostUndertakes = undertakes.Max();
                            if (mostUndertakes > 0)
                            {
                                undertakeCarIdx = undertakes.ToList().IndexOf(mostUndertakes);
                                undertakeCarNumStr = g_Drivers[undertakeCarIdx].CarNumStr;
                                vaProxy.SetText("~~MostUndertakesCarNum", undertakeCarNumStr);
                                vaProxy.SetInt("~~UndertakeMarkerCount", undertakes.Sum());
                            }
                            else
                            {
                                vaProxy.SetText("~~MostOvertakesCarNum", "-1");
                                vaProxy.SetInt("~~OvertakeMarkerCount", 0);
                            }

                            mostIncidents = incidents.Max();
                            if (mostIncidents > 0)
                            {
                                incidentCarIdx = incidents.ToList().IndexOf(mostIncidents);
                                incidentCarNumStr = g_Drivers[incidentCarIdx].CarNumStr;
                                vaProxy.SetText("~~MostIncidentsCarNum", incidentCarNumStr);
                                vaProxy.SetInt("~~IncidentMarkerCount", incidents.Sum());
                            }
                            else
                            {
                                vaProxy.SetText("~~MostIncidentsCarNum", "-1");
                                vaProxy.SetInt("~~IncidentMarkerCount", 0); 
                            }

                            mostBroadcasts = broadcasts.Max();
                            if (mostBroadcasts > 0)
                            {
                                broadcastCarIdx = broadcasts.ToList().IndexOf(mostBroadcasts);
                                broadcastCarNumStr = g_Drivers[broadcastCarIdx].CarNumStr;
                                vaProxy.SetText("~~MostBroadcastsCarNum", broadcastCarNumStr);
                                vaProxy.SetInt("~~RadioMarkerCount", broadcasts.Sum());
                            }
                            else
                            {
                                vaProxy.SetText("~~MostBroadcastsCarNum", "-1");
                                vaProxy.SetInt("~~RadioMarkerCount", 0);
                            }

                            mostManuals = manuals.Max();
                            if (mostManuals > 0)
                            {
                                manualCarIdx = manuals.ToList().IndexOf(mostManuals);
                                manualCarNumStr = g_Drivers[manualCarIdx].CarNumStr;
                                vaProxy.SetText("~~MostManualsCarNum", manualCarNumStr);
                                vaProxy.SetInt("~~ManualMarkerCount", manuals.Sum());
                            }
                            else
                            {
                                vaProxy.SetText("~~MostManualsCarNum", "-1");
                                vaProxy.SetInt("~~ManualMarkerCount", 0);
                            }

                            vaProxy.SetInt("~~MarkerCount", sessionMarkers);

                            summaryStr += $"There are {sessionMarkers} markers in this session.\n";
                            if (overtakes.Sum() > 0)
                            {
                                summaryStr += overtakes.Sum() + " overtakes, with car " + overtakeCarNumStr + " having the most at " + mostOvertakes + ".\n";
                            }
                            if (undertakes.Sum() > 0)
                            {
                                summaryStr += undertakes.Sum() + " undertakes, with car " + undertakeCarNumStr + " having the most at " + mostUndertakes + ".\n";
                            }
                            if (incidents.Sum() > 0)
                            {
                                summaryStr += incidents.Sum() + " incidents, with car " + incidentCarNumStr + " having the most at " + mostIncidents + ".\n";
                            }
                            if (broadcasts.Sum() > 0)
                            {
                                summaryStr += broadcasts.Sum() + " radio broadcasts, with car " + broadcastCarNumStr + " having the most at " + mostBroadcasts + ".\n";
                            }
                            if (starts > 0)
                            {
                                summaryStr += "The race start was also set as a marker.";
                            }
                        }
                        else
                        {
                            summaryStr = "There are no markers.";
                            vaProxy.SetText("~~MostOvertakesCarNum", "-1");
                            vaProxy.SetText("~~MostUndertakesCarNum", "-1");
                            vaProxy.SetText("~~MostIncidentsCarNum", "-1");
                            vaProxy.SetText("~~MostBroadcastsCarNum", "-1");
                            vaProxy.SetText("~~MostManualsCarNum", "-1");
                            vaProxy.SetInt("~~MarkerCount", 0);
                        }

                        vaProxy.SetText("~~MarkerSummary", summaryStr);
                        vaProxy.WriteToLog("Marker Summary:  " + summaryStr, "purple");
                        break;
                    }

                case "Marker_Summary_CarNumber":
                    {
                        string summaryStr = "There are no markers.";
                        int carNumberMarkerCount = 0;
                        int carNumberIncidentMarkerCount = 0;
                        int carNumberOvertakeMarkerCount = 0;
                        int carNumberUndertakeMarkerCount = 0;
                        int carNumberRadioMarkerCount = 0;
                        int carNumberManualMarkerCount = 0;
                        vaProxy.SetInt("~~CarNumberMarkerCount", carNumberMarkerCount);
                        vaProxy.SetInt("~~CarNumberIncidentMarkerCount", carNumberIncidentMarkerCount);
                        vaProxy.SetInt("~~CarNumberOvertakeMarkerCount", carNumberOvertakeMarkerCount);
                        vaProxy.SetInt("~~CarNumberUndertakeMarkerCount", carNumberUndertakeMarkerCount);
                        vaProxy.SetInt("~~CarNumberRadioMarkerCount", carNumberRadioMarkerCount);
                        vaProxy.SetInt("~~CarNumberManualMarkerCount", carNumberManualMarkerCount);

                        string carNumberStr  = vaProxy.GetText("~~CarNumber");
                        if (carNumberStr == null)
                        {
                            vaProxy.WriteToLog("Couldnt get marker summary for car number, no {INT:~~CarNumber} variable.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }
                        int carNumberRaw = CheckCarNumber(carNumberStr);
                        if (carNumberRaw < 0)
                        {
                            vaProxy.SetText("~~CarNumber", "-1");
                            PrintDrivers();
                            vaProxy.WriteToLog("Couldnt find car: " + carNumberStr + ".  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                            break;
                        }

                        if (g_Markers.Any() && g_Connected && g_Drivers != null)
                        {
                            int carNumberIdx = g_Drivers.FirstOrDefault(d => d.CarNumberRaw == carNumberRaw).CarIdx;
                            foreach (Event marker in g_Markers)
                            {
                                if (marker.Session == g_CurrentSessionNum &&  marker.CarIdx == carNumberIdx)
                                {
                                    switch (marker.EventType)
                                    {
                                        case MarkerType.Overtake:
                                            carNumberMarkerCount++;
                                            carNumberOvertakeMarkerCount++;
                                            break;

                                        case MarkerType.Undertake:
                                            carNumberMarkerCount++;
                                            carNumberUndertakeMarkerCount++;
                                            break;

                                        case MarkerType.Incident:
                                            carNumberMarkerCount++;
                                            carNumberIncidentMarkerCount++;
                                            break;

                                        case MarkerType.Radio:
                                            carNumberMarkerCount++;
                                            carNumberRadioMarkerCount++;
                                            break;

                                        case MarkerType.Manual:
                                            carNumberMarkerCount++;
                                            carNumberManualMarkerCount++;
                                            break;
                                    }

                                }
                            } 
                            summaryStr = "There are " + carNumberMarkerCount + " markers for CarNumber " + carNumberStr + ".\n";
                            if (carNumberOvertakeMarkerCount > 0)
                            {
                                summaryStr += carNumberOvertakeMarkerCount + " overtakes.\n";
                            }
                            if (carNumberUndertakeMarkerCount > 0)
                            {
                                summaryStr += carNumberUndertakeMarkerCount + " undertakes.\n";
                            }
                            if (carNumberIncidentMarkerCount > 0)
                            {
                                summaryStr += carNumberIncidentMarkerCount + " incidents.\n";
                            }
                            if (carNumberRadioMarkerCount > 0)
                            {
                                summaryStr += carNumberRadioMarkerCount + " radio broadcasts.\n";
                            }
                            if (carNumberManualMarkerCount > 0)
                            {
                                summaryStr += carNumberManualMarkerCount + " manual markers.\n";
                            }

                            vaProxy.SetInt("~~CarNumberMarkerCount", carNumberMarkerCount);
                            vaProxy.SetInt("~~CarNumberIncidentMarkerCount", carNumberIncidentMarkerCount);
                            vaProxy.SetInt("~~CarNumberOvertakeMarkerCount", carNumberOvertakeMarkerCount);
                            vaProxy.SetInt("~~CarNumberUndertakeMarkerCount", carNumberUndertakeMarkerCount);
                            vaProxy.SetInt("~~CarNumberRadioMarkerCount", carNumberRadioMarkerCount);
                            vaProxy.SetInt("~~CarNumberManualMarkerCount", carNumberManualMarkerCount);

                        }
                        vaProxy.SetText("~~CarNumberMarkerSummary", summaryStr);
                        //vaProxy.WriteToLog("CarNumberMarker Summary:  " + summaryStr, "purple");
                        break;
                    }

                case "Jump_ToLive":
                    _iRSDKWrapper.Replay.JumpToLive();
                    //vaProxy.WriteToLog("Jump to live");
                    break;

                case "Jump_ToBeginning":
                    _iRSDKWrapper.Replay.JumpToStart();
                    //vaProxy.WriteToLog("Jump to beginning of recording");
                    SeekWait();
                    _iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                    break;

                case "iRacingIncident_Previous":
                    //iRacing Incident 
                    _iRSDKWrapper.Replay.Jump(ReplaySearchModeTypes.PreviousIncident);
                    SeekWait();
                    _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearchSessionTime, (int)g_ReplaySessionNum, (g_ReplaySessionTime - cIncidentBufferSecs) * 1000);
                    //Always come out of this playing
                    _iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                    //vaProxy.WriteToLog("Playing previous iRacing incident");
                    break;

                case "iRacingIncident_Next":
                    //iRacing Incident 
                    _iRSDKWrapper.Replay.Jump(ReplaySearchModeTypes.NextIncident);
                    SeekWait();
                    _iRSDKWrapper.Sdk.BroadcastMessage(BroadcastMessageTypes.ReplaySearchSessionTime, (int)g_ReplaySessionNum, (g_ReplaySessionTime - cIncidentBufferSecs) * 1000);
                    //Always come out of this playing
                    _iRSDKWrapper.Replay.SetPlaybackSpeed(1);
                    //vaProxy.WriteToLog("Playing previous iRacing incident");
                    break;

                case "Watch_MyCar":
                    if (g_PlayerCarIdx > 0 && g_Drivers.First(d => d.CarIdx == g_PlayerCarIdx).IsSpectator != 1)
                    {
                        _iRSDKWrapper.Camera.SwitchToCar(cWatchMyCarIdx, 0);
                        //vaProxy.WriteToLog("Watching your car", "purple");
                    }
                    else if (g_TrackPositions != null)
                    {
                        int leaderCarNumRaw = g_TrackPositions.FirstOrDefault(d => d.Position == 1).Driver.CarNumberRaw;
                        _iRSDKWrapper.Camera.SwitchToCar(leaderCarNumRaw, 0);
                        vaProxy.WriteToLog("Watch_MyCar: No Player Car, switching to leader.", "orange");
                    }
                    else if (g_CarIdxTrackSurface[0] != TrackSurfaces.NotInWorld)
                    {
                        _iRSDKWrapper.Camera.SwitchToCar(0, 0);
                        vaProxy.WriteToLog("Watch_MyCar: Player car not in world, no position data, switching to pace car", "red");
                    }
                    else
                    {
                        _iRSDKWrapper.Camera.SwitchToCar(1, 0);
                        vaProxy.WriteToLog("Watch_MyCar: No Player Car, no pace car, no position data, switching to CarIdx 1", "red");
                    }

                    break;

                case "Watch_MostExciting":
                    _iRSDKWrapper.Camera.SwitchToCar(cWatchMostExcitingCarIdx, 0);
                    //vaProxy.WriteToLog("Watching most exciting", "purple");
                    break;

                case "Print_Info":
                    PrintInfo();
                    break;

                case "Print_Cameras":
                    PrintCameras();
                    break;

                case "Print_Drivers":
                    PrintDrivers();
                    break;

                default:
                    ShowUsage();
                    vaProxy.WriteToLog("[" + vaProxy.Context + "] is not a recognized VAPlugin_RobertsmaniaReplay command.  (" + vaProxy.Command.Name() + ") attempted to use it.", "red");
                    break;
            }
        }
    }
}
