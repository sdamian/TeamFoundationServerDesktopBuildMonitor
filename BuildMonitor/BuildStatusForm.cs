using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BuildMonitor.Domain;
using BuildMonitor.Plugin;
using BuildMonitor.Properties;
using BuildMonitor.Services;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using BuildStatus = Microsoft.TeamFoundation.Build.Client.BuildStatus;

namespace BuildMonitor
{
    public partial class BuildStatusForm : Form
    {
        private readonly IResourceManager _resourceManager;
        private readonly List<ServerBuild> _serverBuilds = new List<ServerBuild>();
        private ImageIndex _lastBuildStatus;
        private Thread _monitorThread;

        public BuildStatusForm()
        {
            _resourceManager = new ResourceManager();
            InitializeComponent();
            var imageList = new ImageList();
            imageList.Images.AddRange(_resourceManager.GetIcons().ToArray());
            BuildListView.SmallImageList = imageList;

            if (Settings.Default.Builds != null)
            {
                _serverBuilds.AddRange(Settings.Default.Builds);
            }
            MonitorBuilds();
        }

        [Import]
        public IEnumerable<AbstractNotifier> Notifiers { get; set; }

        private static ImageIndex GetImageIndex(BuildStatus buildStatus)
        {
            switch (buildStatus)
            {
                case BuildStatus.Succeeded:
                    return ImageIndex.Green;
                case BuildStatus.InProgress:
                    return ImageIndex.Yellow;
                default:
                    return ImageIndex.Red;
            }
        }

        private void AddBuilds(IEnumerable<ServerBuild> serverBuilds)
        {
            if (serverBuilds == null)
            {
                return;
            }
            foreach (ServerBuild serverBuild in serverBuilds)
            {
                int count =
                    (from sb in this._serverBuilds
                        where sb.ServerUrl == serverBuild.ServerUrl && sb.BuildUrl == serverBuild.BuildUrl
                        select sb).Count();
                if (count == 0)
                {
                    this._serverBuilds.Add(serverBuild);
                }
            }
        }

        private void AddItem(ListViewItem listViewItem)
        {
            if (BuildListView.InvokeRequired)
            {
                AddItemCallback d = AddItem;
                Invoke(d, listViewItem);
            }
            else
            {
                BuildListView.Items.Add(listViewItem);
            }
        }

        private void AddItems(IEnumerable<ListViewItem> listViewItems)
        {
            // this.BuildListView.BeginUpdate();
            foreach (ListViewItem item in listViewItems)
            {
                AddItem(item);
            }
            // this.BuildListView.EndUpdate();
        }

        private void AnnounceCulprit(string culprit)
        {
            foreach (AbstractNotifier notifier in Notifiers)
            {
                notifier.AnnounceBrokenBuild("Global", culprit, DateTime.Now);
            }
        }

        private void BuildListView_KeyDown(object sender, KeyEventArgs e)
        {
            _monitorThread.Abort();
            if (e.KeyCode == Keys.Delete)
            {
                foreach (ListViewItem selectedItem in BuildListView.SelectedItems)
                {
                    var selectedServerBuild = (ServerBuild) selectedItem.Tag;
                    _serverBuilds.Remove(selectedServerBuild);
                }
            }
            SaveBuildList();
            MonitorBuilds();
        }

        private void ClearItems()
        {
            if (BuildListView.InvokeRequired)
            {
                ClearItemsCallback d = ClearItems;
                Invoke(d, new object[] {});
            }
            else
            {
                BuildListView.Items.Clear();
            }
        }

        private IBuildServer GetBuildServer(string serverUri)
        {
            TfsTeamProjectCollection teamProjectCollection =
                TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(serverUri));
            var buildServer = teamProjectCollection.GetService<IBuildServer>();
            return buildServer;
        }

        private void ListBuildsForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                ShowInTaskbar = false;
            }
            else
            {
                ShowInTaskbar = true;
            }
        }

        private void MonitorBuilds()
        {
            _monitorThread = new Thread(ShowBuilds);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();
        }

        private void SaveBuildList()
        {
            Settings.Default.Builds = new ServerBuildCollection(_serverBuilds);
            Settings.Default.Save();
        }

        private void SetSystemTrayIcon(ImageIndex imageIndex)
        {
            string imageResourceString;
            switch (imageIndex)
            {
                case ImageIndex.Green:
                    imageResourceString = "BuildMonitor.Images.GreenBall.ico";
                    break;
                case ImageIndex.Yellow:
                    imageResourceString = "BuildMonitor.Images.GreenBall.ico";
                    break;
                default:
                    imageResourceString = "BuildMonitor.Images.RedBall.ico";
                    break;
            }
            Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(imageResourceString);

            systemTrayNotifyIcon.Icon = new Icon(iconStream);
        }

        private void ShowBuilds()
        {
            const int LoopSpeed = 10000;

            while (true)
            {
                try
                {
                    ClearItems();
                    var overallImageIndex = ImageIndex.Green;
                    string culprit = string.Empty;

                    var listViewItems = new List<ListViewItem>();

                    foreach (ServerBuild serverBuild in _serverBuilds)
                    {
                        IBuildServer buildServer = GetBuildServer(serverBuild.ServerUrl);
                        IBuildDefinition buildDefinition =
                            buildServer.QueryBuildDefinitionsByUri(new[] {new Uri(serverBuild.BuildUrl)})[0];

                        var viewModel = new BuildInformationListViewModel {Name = buildDefinition.Name};

                        IBuildDetail lastBuildDetails =
                            buildServer.QueryBuildsByUri(new[] {buildDefinition.LastBuildUri}, null, QueryOptions.None)[
                                0];

                        if (lastBuildDetails != null)
                        {
                            viewModel.Status = lastBuildDetails.Status.ToString();
                            viewModel.BuildTime = lastBuildDetails.FinishTime.ToString();
                            viewModel.SourceVersion = lastBuildDetails.SourceGetVersion;
                        }

                        ImageIndex imageIndex = lastBuildDetails == null
                            ? ImageIndex.Red
                            : GetImageIndex(lastBuildDetails.Status);
                        var listViewItem = new ListViewItem(viewModel.ToStringArray(), (int) imageIndex)
                        {
                            Tag = serverBuild
                        };
                        listViewItems.Add(listViewItem);
                        if (imageIndex == ImageIndex.Red)
                        {
                            overallImageIndex = ImageIndex.Red;
                            culprit = lastBuildDetails == null ? "Unable to find build" : lastBuildDetails.RequestedFor;
                        }
                        else if (imageIndex == ImageIndex.Yellow && overallImageIndex == ImageIndex.Green)
                        {
                            overallImageIndex = ImageIndex.Yellow;
                        }
                    }
                    AddItems(listViewItems);
                    SetSystemTrayIcon(overallImageIndex);
                    if (overallImageIndex != _lastBuildStatus)
                    {
                        AnnounceCulprit(culprit);
                        _lastBuildStatus = overallImageIndex;
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }
                Thread.Sleep(LoopSpeed);
            }
        }

        private void btnAddBuilds_Click(object sender, EventArgs e)
        {
            _monitorThread.Abort();
            var selectBuildsForm = new SelectBuildsForm();
            selectBuildsForm.ShowDialog();
            AddBuilds(selectBuildsForm.SelectedServerBuilds);
            SaveBuildList();
            MonitorBuilds();
        }

        private void systemTrayNotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
        }

        private void BuildListView_SelectedIndexChanged(object sender, EventArgs e)
        {
        }

        private delegate void AddItemCallback(ListViewItem listViewItem);

        private delegate void ClearItemsCallback();

        private enum ImageIndex
        {
            Green = 0,
            Yellow = 1,
            Red = 2
        }
    }
}