using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using Gw2Sharp;
using Gw2Sharp.Models;
using Gw2Sharp.Mumble;
using Gw2Sharp.WebApi.V2;
using Gw2Sharp.WebApi.V2.Models;

namespace Gw2MumbleLinkReader
{
    public partial class ReaderForm : Form
    {
        private readonly Gw2Client client = new();

        private Thread? apiThread;
        private Thread? mumbleLoopThread;
        private bool stopRequested;

        private readonly Queue<int> apiMapDownloadQueue = new();
        private readonly HashSet<int> apiMapDownloadBusy = new();
        private readonly ConcurrentDictionary<int, Map> maps = new();
        private readonly ConcurrentDictionary<(int, int), IApiV2ObjectList<ContinentFloorRegionMapPoi>> pois = new();
        private readonly AutoResetEvent apiMapDownloadEvent = new(false);

        private readonly System.Windows.Forms.Timer mClearStatusTimer = new();

        public ReaderForm()
        {
            this.InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.apiThread = new Thread(this.ApiLoopAsync);
            this.mumbleLoopThread = new Thread(this.MumbleLoop);
            this.mClearStatusTimer.Tick += this.ClearStatus;

            this.apiThread.Start();
            this.mumbleLoopThread.Start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.UpdateStatus("Shutting down");
            this.stopRequested = true;
            this.apiMapDownloadEvent.Set();
        }

        private void UpdateStatus(string? message, TimeSpan? timeToShow = default) =>
            this.labelStatus.Invoke(new Action<string>(m =>
            {
                this.labelStatus.Text = m;
                this.labelStatus.Visible = !string.IsNullOrWhiteSpace(m);

                if (timeToShow?.TotalMilliseconds >= 1)
                {
                    this.mClearStatusTimer.Interval = (int)timeToShow.Value.TotalMilliseconds;
                    this.mClearStatusTimer.Enabled = true;
                }
                else
                {
                    this.mClearStatusTimer.Enabled = false;
                }
            }), message);

        private void ClearStatus(object? sender, EventArgs e) =>
            this.UpdateStatus(null);


        private async void ApiLoopAsync()
        {
            while (!this.stopRequested)
            {
                this.apiMapDownloadEvent.WaitOne();
                if (this.stopRequested)
                    break;

                int mapId = this.apiMapDownloadQueue.Dequeue();
                this.UpdateStatus($"Downloading API information for map {mapId}");

                var map = await this.client.WebApi.V2.Maps.GetAsync(mapId).ConfigureAwait(false);
                this.maps[mapId] = map;

                foreach (int floorId in map.Floors)
                {
                    if (this.pois.ContainsKey((floorId, mapId)))
                        continue;

                    this.UpdateStatus($"Downloading API information for map {mapId} on continent {map.ContinentId} and floor {floorId}");
                    this.pois[(floorId, mapId)] = await this.client.WebApi.V2.Continents[map.ContinentId].Floors[floorId].Regions[map.RegionId].Maps[mapId].Pois.AllAsync().ConfigureAwait(false);
                }

                this.apiMapDownloadBusy.Remove(mapId);
                this.UpdateStatus($"Map download status: {map.HttpResponseInfo!.CacheState}", TimeSpan.FromSeconds(3));
            }
        }

        private void MumbleLoop()
        {
            int mapId = 0;

            do
            {
                bool shouldRun = true;
                this.client.Mumble.Update();
                if (!this.client.Mumble.IsAvailable)
                    shouldRun = false;

                int newMapId = this.client.Mumble.MapId;
                if (newMapId == 0)
                    shouldRun = false;

                if (shouldRun)
                {
                    if (newMapId != mapId && !this.apiMapDownloadBusy.Contains(mapId))
                    {
                        this.apiMapDownloadBusy.Add(newMapId);
                        this.apiMapDownloadQueue.Enqueue(newMapId);
                        this.apiMapDownloadEvent.Set();
                        mapId = newMapId;
                    }

                    try
                    {
                        this.Invoke(new Action<IGw2MumbleClient>(m =>
                        {
                            this.textBoxVersion.Text = m.Version.ToString();
                            this.textBoxTick.Text = m.Tick.ToString();
                            this.textBoxAvatarPosition1.Text = NumberFormat(m.AvatarPosition.X, "F3");
                            this.textBoxAvatarPosition2.Text = NumberFormat(m.AvatarPosition.Y, "F3");
                            this.textBoxAvatarPosition3.Text = NumberFormat(m.AvatarPosition.Z, "F3");
                            this.textBoxAvatarFront1.Text = NumberFormat(m.AvatarFront.X, "F3");
                            this.textBoxAvatarFront2.Text = NumberFormat(m.AvatarFront.Y, "F3");
                            this.textBoxAvatarFront3.Text = NumberFormat(m.AvatarFront.Z, "F3");
                            this.textBoxName.Text = m.Name;
                            this.textBoxCameraPosition1.Text = NumberFormat(m.CameraPosition.X, "F3");
                            this.textBoxCameraPosition2.Text = NumberFormat(m.CameraPosition.Y, "F3");
                            this.textBoxCameraPosition3.Text = NumberFormat(m.CameraPosition.Z, "F3");
                            this.textBoxCameraFront1.Text = NumberFormat(m.CameraFront.X, "F3");
                            this.textBoxCameraFront2.Text = NumberFormat(m.CameraFront.Y, "F3");
                            this.textBoxCameraFront3.Text = NumberFormat(m.CameraFront.Z, "F3");

                            this.textBoxRawIdentity.Text = m.RawIdentity;
                            this.textBoxCharacterName.Text = m.CharacterName;
                            this.textBoxRace.Text = m.Race.ToString();
                            this.textBoxSpecialization.Text = m.Specialization.ToString();
                            this.textBoxTeamColorId.Text = m.TeamColorId.ToString();
                            this.checkBoxCommander.Checked = m.IsCommander;
                            this.textBoxFieldOfView.Text = NumberFormat(m.FieldOfView, "F3");
                            this.textBoxUiSize.Text = m.UiSize.ToString();

                            this.textBoxServerAddress.Text = $@"{m.ServerAddress}:{m.ServerPort}";
                            this.textBoxMapId.Text = m.MapId.ToString();
                            this.textBoxMapType.Text = m.MapType.ToString();
                            this.textBoxShardId.Text = m.ShardId.ToString();
                            this.textBoxInstance.Text = m.Instance.ToString();
                            this.textBoxBuildId.Text = m.BuildId.ToString();
                            this.checkBoxUiStateMapOpen.Checked = m.IsMapOpen;
                            this.checkBoxUiStateCompassTopRight.Checked = m.IsCompassTopRight;
                            this.checkBoxUiStateCompassRotationEnabled.Checked = m.IsCompassRotationEnabled;
                            this.checkBoxUiStateGameFocus.Checked = m.DoesGameHaveFocus;
                            this.checkBoxUiStateCompetitive.Checked = m.IsCompetitiveMode;
                            this.checkBoxUiStateInputFocus.Checked = m.DoesAnyInputHaveFocus;
                            this.checkBoxUiStateInCombat.Checked = m.IsInCombat;
                            this.textBoxCompassWidth.Text = m.Compass.Width.ToString();
                            this.textBoxCompassHeight.Text = m.Compass.Height.ToString();
                            this.textBoxCompassRotation.Text = NumberFormat(m.CompassRotation, "F0");
                            this.textBoxPlayerCoordsX.Text = NumberFormat(m.PlayerLocationMap.X, "F0");
                            this.textBoxPlayerCoordsY.Text = NumberFormat(m.PlayerLocationMap.Y, "F0");
                            this.textBoxMapCenterX.Text = NumberFormat(m.MapCenter.X, "F0");
                            this.textBoxMapCenterY.Text = NumberFormat(m.MapCenter.Y, "F0");
                            this.textBoxMapScale.Text = NumberFormat(m.MapScale, "F3");
                            this.textBoxProcessId.Text = m.ProcessId.ToString();
                            this.textBoxMount.Text = m.Mount.ToString();

                            if (!this.maps.TryGetValue(m.MapId, out var map))
                                return;

                            this.textBoxMapName.Text = map.Name;

                            var mapPosition = m.AvatarPosition.ToMapCoords(CoordsUnit.Mumble);
                            this.textBoxMapPosition1.Text = NumberFormat(mapPosition.X, "F3");
                            this.textBoxMapPosition2.Text = NumberFormat(mapPosition.Y, "F3");
                            this.textBoxMapPosition3.Text = NumberFormat(mapPosition.Z, "F3");

                            var continentPosition = m.AvatarPosition.ToContinentCoords(CoordsUnit.Mumble, map.MapRect, map.ContinentRect);
                            this.textBoxContinentPosition1.Text = NumberFormat(continentPosition.X, "F0");
                            this.textBoxContinentPosition2.Text = NumberFormat(continentPosition.Y, "F0");
                            this.textBoxContinentPosition3.Text = NumberFormat(continentPosition.Z, "F0");

                            ContinentFloorRegionMapPoi? closestWaypoint = null;
                            Coordinates2 closestWaypointPosition = default;
                            double closestWaypointDistance = double.MaxValue;
                            ContinentFloorRegionMapPoi? closestPoi = null;
                            Coordinates2 closestPoiPosition = default;
                            double closestPoiDistance = double.MaxValue;
                            foreach (int floorId in map.Floors)
                            {
                                if (!this.pois.TryGetValue((floorId, map.Id), out var mapPois))
                                    continue;

                                foreach (ContinentFloorRegionMapPoi poi in mapPois)
                                {
                                    double distance = Math.Sqrt(Math.Pow(Math.Abs(continentPosition.X - poi.Coord.X), 2) + Math.Pow(Math.Abs(continentPosition.Z - poi.Coord.Y), 2));
                                    switch (poi.Type.Value)
                                    {
                                        case PoiType.Waypoint when distance < closestWaypointDistance:
                                            closestWaypointPosition = poi.Coord;
                                            closestWaypointDistance = distance;
                                            closestWaypoint = poi;
                                            break;
                                        case PoiType.Landmark when distance < closestPoiDistance:
                                            closestPoiPosition = poi.Coord;
                                            closestPoiDistance = distance;
                                            closestPoi = poi;
                                            break;
                                    }
                                }
                            }

                            if (closestWaypoint is not null)
                            {
                                this.textBoxWaypoint.Text = closestWaypoint.Name;
                                this.textBoxWaypointLink.Text = closestWaypoint.ChatLink;
                                this.textBoxWaypointContinentDistance.Text = NumberFormat(closestWaypointDistance, "F3");
                                this.textBoxWaypointContinentPosition1.Text = NumberFormat(closestWaypoint.Coord.X, "F0");
                                this.textBoxWaypointContinentPosition2.Text = NumberFormat(closestWaypoint.Coord.Y, "F0");
                                double angle = GetAngle(continentPosition, closestWaypointPosition);
                                this.textBoxWaypointDirection1.Text = GetDirectionFromAngle(angle).ToString();
                                this.textBoxWaypointDirection2.Text = NumberFormat(angle, "F0");
                            }
                            else
                            {
                                this.textBoxWaypoint.Text = string.Empty;
                                this.textBoxWaypointLink.Text = string.Empty;
                                this.textBoxWaypointContinentDistance.Text = string.Empty;
                                this.textBoxWaypointContinentPosition1.Text = string.Empty;
                                this.textBoxWaypointContinentPosition2.Text = string.Empty;
                                this.textBoxWaypointDirection1.Text = string.Empty;
                                this.textBoxWaypointDirection2.Text = string.Empty;
                            }

                            if (closestPoi is not null)
                            {
                                this.textBoxPoi.Text = closestPoi.Name;
                                this.textBoxPoiLink.Text = closestPoi.ChatLink;
                                this.textBoxPoiContinentDistance.Text = NumberFormat(closestPoiDistance, "F3");
                                this.textBoxPoiContinentPosition1.Text = NumberFormat(closestPoi.Coord.X, "F0");
                                this.textBoxPoiContinentPosition2.Text = NumberFormat(closestPoi.Coord.Y, "F0");
                                double angle = GetAngle(continentPosition, closestPoiPosition);
                                this.textBoxPoiDirection1.Text = GetDirectionFromAngle(angle).ToString();
                                this.textBoxPoiDirection2.Text = NumberFormat(angle, "F0");
                            }
                            else
                            {
                                this.textBoxPoi.Text = string.Empty;
                                this.textBoxPoiLink.Text = string.Empty;
                                this.textBoxPoiContinentDistance.Text = string.Empty;
                                this.textBoxPoiContinentPosition1.Text = string.Empty;
                                this.textBoxPoiContinentPosition2.Text = string.Empty;
                                this.textBoxPoiDirection1.Text = string.Empty;
                                this.textBoxPoiDirection2.Text = string.Empty;
                            }
                        }), this.client.Mumble);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The application is likely closing
                        break;
                    }
                }

                Thread.Sleep(1000 / 60);
            } while (!this.stopRequested);
        }

        private static string NumberFormat(double val, string fmt) => val.ToString(fmt, CultureInfo.InvariantCulture);

        private static double GetAngle(Coordinates3 pos1, Coordinates2 pos2) => Math.Atan2(pos1.Z - pos2.Y, pos1.X - pos2.X) * 180 / Math.PI;

        private static Direction GetDirectionFromAngle(double angle) => angle switch
        {
            < -168.75 => Direction.West,
            < -146.25 => Direction.WestNorthWest,
            < -123.75 => Direction.NorthWest,
            < -101.25 => Direction.NorthNorthWest,
            < -78.75 => Direction.North,
            < -56.25 => Direction.NorthNorthEast,
            < -33.75 => Direction.NorthEast,
            < -11.25 => Direction.EastNorthEast,
            < 11.25 => Direction.East,
            < 33.75 => Direction.EastSouthEast,
            < 56.25 => Direction.SouthEast,
            < 78.78 => Direction.SouthSouthEast,
            < 101.25 => Direction.South,
            < 123.75 => Direction.SouthSouthWest,
            < 146.25 => Direction.SouthWest,
            < 168.75 => Direction.WestSouthWest,
            _ => Direction.West
        };

        private enum Direction
        {
            North,
            NorthNorthEast,
            NorthEast,
            EastNorthEast,
            East,
            EastSouthEast,
            SouthEast,
            SouthSouthEast,
            South,
            SouthSouthWest,
            SouthWest,
            WestSouthWest,
            West,
            WestNorthWest,
            NorthWest,
            NorthNorthWest
        }
    }
}
