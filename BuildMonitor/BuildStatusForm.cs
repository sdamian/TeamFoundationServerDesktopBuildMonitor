﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BuildMonitor.Domain;
using BuildMonitor.Plugin;
using BuildMonitor.Properties;
using BuildMonitor.Services;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.Win32;
using Tools;
using BuildStatus = Microsoft.TeamFoundation.Build.Client.BuildStatus;

namespace BuildMonitor
{
    public partial class BuildStatusForm : Form
    {
        private readonly List<ServerBuild> serverBuilds = new List<ServerBuild>();
        private ImageIndex lastBuildStatus;
        private Thread monitorThread;
        private readonly IResourceManager resourceManager;

        public BuildStatusForm()
        {
            resourceManager = new ResourceManager();
            InitializeComponent();
            var imageList = new ImageList();
            imageList.Images.AddRange(resourceManager.GetIcons().ToArray());
            BuildListView.SmallImageList = imageList;

            if (Settings.Default.Builds != null)
            {
                serverBuilds.AddRange(Settings.Default.Builds);
            }
            MonitorBuilds();
        }

        [Import]
        public IEnumerable<AbstractNotifier> Notifiers { get; set; }

        private delegate void AddItemCallback(ListViewItem listViewItem);

        private delegate void ClearItemsCallback();

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

        private enum ImageIndex
        {
            Green = 0, 
            Yellow = 1, 
            Red = 2
        }

        private static void SetRadiatorBuildStatusColor(ImageIndex overallImageIndex)
        {
            var userName = ConfigurationManager.AppSettings["RadiatorUserName"];
            var domain = ConfigurationManager.AppSettings["RadiatorDomain"];
            var password = ConfigurationManager.AppSettings["RadiatorPassword"];
            using (new Impersonator(userName, domain, password))
            {
                var buildStatusColorCss = string.Format(".buildStatusColor {{ background-color: {0}; }}",
                    overallImageIndex);

                var cssFilePath = ConfigurationManager.AppSettings["RadiatorCssFilePath"];
                var cssFileContents = File.ReadAllText(cssFilePath);

                cssFileContents = cssFileContents.Replace(".buildStatusColor { background-color: Red; }", buildStatusColorCss);
                cssFileContents = cssFileContents.Replace(".buildStatusColor { background-color: Yellow; }", buildStatusColorCss);
                cssFileContents = cssFileContents.Replace(".buildStatusColor { background-color: Green; }", buildStatusColorCss);

                File.WriteAllText(cssFilePath, cssFileContents);
            }
        }

        private void AddBuilds(IEnumerable<ServerBuild> serverBuilds)
        {
            if (serverBuilds == null)
            {
                return;
            }
            foreach (var serverBuild in serverBuilds)
            {
                var count =
                    (from sb in this.serverBuilds
                     where sb.ServerUrl == serverBuild.ServerUrl && sb.BuildUrl == serverBuild.BuildUrl
                     select sb).Count();
                if (count == 0)
                {
                    this.serverBuilds.Add(serverBuild);
                }
            }
        }

        private void AddItem(ListViewItem listViewItem)
        {
            if (BuildListView.InvokeRequired)
            {
                AddItemCallback d = AddItem;
                Invoke(d, new object[] { listViewItem });
            }
            else
            {
                BuildListView.Items.Add(listViewItem);
            }
        }

        private void AddItems(IEnumerable<ListViewItem> listViewItems)
        {
            // this.BuildListView.BeginUpdate();
            foreach (var item in listViewItems)
            {
                this.AddItem(item);
            }
            // this.BuildListView.EndUpdate();
        }

        private void AnnounceCulprit(string culprit)
        {
            foreach (var notifier in Notifiers)
            {
                notifier.AnnounceBrokenBuild("Global", culprit, DateTime.Now);
            }
        }

        private void BuildListView_KeyDown(object sender, KeyEventArgs e)
        {
            this.monitorThread.Abort();
            if (e.KeyCode == Keys.Delete)
            {
                foreach (ListViewItem selectedItem in BuildListView.SelectedItems)
                {
                    var selectedServerBuild = (ServerBuild)selectedItem.Tag;
                    this.serverBuilds.Remove(selectedServerBuild);
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
                Invoke(d, new object[] { });
            }
            else
            {
                BuildListView.Items.Clear();
            }
        }

        private IBuildServer GetBuildServer(string serverUri)
        {
            var teamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(serverUri));
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
            this.monitorThread = new Thread(ShowBuilds);
            this.monitorThread.Start();
        }

        private void SaveBuildList()
        {
            Settings settings = Settings.Default;
            settings.Builds = new ServerBuildCollection(serverBuilds);

            XmlSerializer serializer = new XmlSerializer(typeof(ServerBuildCollection));

            var stringBuilder = new StringBuilder();
            var stringWriter = new StringWriter(stringBuilder);
            serializer.Serialize(stringWriter, settings.Builds);
            var xml = stringBuilder.ToString();

            settings.Save();
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
            var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(imageResourceString);

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
                    var culprit = string.Empty;

                    var listViewItems = new List<ListViewItem>();

                    foreach (var serverBuild in this.serverBuilds)
                    {
                        var buildServer = GetBuildServer(serverBuild.ServerUrl.ToString());
                        var buildDefinition = buildServer.QueryBuildDefinitionsByUri(new[] { new Uri(serverBuild.BuildUrl) })[0];
                        
                        var viewModel = new BuildInformationListViewModel { Name = buildDefinition.Name };

                        var lastBuildDetails =
                            buildServer.QueryBuildsByUri(new[] { buildDefinition.LastBuildUri }, null, QueryOptions.None)[0];

                        if (lastBuildDetails != null)
                        {
                            viewModel.Status = lastBuildDetails.Status.ToString();
                            viewModel.BuildTime = lastBuildDetails.FinishTime.ToString();
                            viewModel.SourceVersion = lastBuildDetails.SourceGetVersion;
                        }

                        var imageIndex = lastBuildDetails == null ? ImageIndex.Red : GetImageIndex(lastBuildDetails.Status);
                        var listViewItem = new ListViewItem(viewModel.ToStringArray(), (int)imageIndex) { Tag = serverBuild };
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
                    if (overallImageIndex != this.lastBuildStatus)
                    {
                        SetRadiatorBuildStatusColor(overallImageIndex);
                        AnnounceCulprit(culprit);
                        this.lastBuildStatus = overallImageIndex;
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
            this.monitorThread.Abort();
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
    }
}