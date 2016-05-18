using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BuildMonitor.Domain;
using BuildMonitor.Properties;
using CCTfsWrapper;
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;

namespace BuildMonitor
{
    public partial class SelectBuildsForm : Form
    {
        private Dictionary<string, string> _buildDictionary;

        public SelectBuildsForm()
        {
            this.InitializeComponent();
        }

        public List<ServerBuild> SelectedServerBuilds { get; set; }

        protected override void OnLoad(EventArgs e)
        {
            lblServerUri.Text = Settings.Default.ServerUrl;
            if (this.lblServerUri.Text != string.Empty)
            {
                this.PopulateProjects();
                var defaultProject = Settings.Default.ProjectName;
                this.TeamProjectDropDown.SelectedText = defaultProject;
                this.ShowBuilds(defaultProject);
            }
            base.OnLoad(e);
        }

        private void AddBuildsButton_Click(object sender, EventArgs e)
        {
            this.SelectedServerBuilds = new List<ServerBuild>();
            foreach (ListViewItem selectedItem in this.BuildsListView.SelectedItems)
            {
                this.SelectedServerBuilds.Add(new ServerBuild(this.lblServerUri.Text, this._buildDictionary[selectedItem.Text]));
            }
            this.Close();
        }

        private void ChangeServerLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var serverUri = InputBox.Show("Server Uri:", "Enter Server Uri");
            if (!string.IsNullOrEmpty(serverUri))
            {
                Settings.Default.ServerUrl = serverUri;
                this.lblServerUri.Text = serverUri;
                this.BuildsListView.Clear();
                this.PopulateProjects();
            }
        }

        private IBuildServer GetBuildServer()
        {
            var teamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(this.lblServerUri.Text));
            var buildServer = teamProjectCollection.GetService<IBuildServer>();
            return buildServer;
        }

        private void PopulateProjects()
        {
            this.TeamProjectDropDown.Items.Clear();
            var tfs = TeamFoundationServerFactory.GetServer(this.lblServerUri.Text);
            var projectCollection = (ICommonStructureService)tfs.GetService(typeof(ICommonStructureService));
            foreach (var projectInfo in projectCollection.ListProjects())
            {
                this.TeamProjectDropDown.Items.Add(projectInfo.Name);
            }
            this.TeamProjectDropDown.Enabled = true;
        }

        private void ShowBuilds(string projectName)
        {
            this.BuildsListView.Items.Clear();
            var buildServer = this.GetBuildServer();
            var buildDefinitions = buildServer.QueryBuildDefinitions(projectName);
            this._buildDictionary = new Dictionary<string, string>();
            foreach (var buildDetail in buildDefinitions)
            {
                this._buildDictionary.Add(buildDetail.Name, buildDetail.Uri.ToString());
                var buildInformation = new string[2];
                buildInformation[0] = buildDetail.Name;
                buildInformation[1] = buildDetail.Uri.ToString();
                this.BuildsListView.Items.Add(buildDetail.Name);
            }

        }

        private void ShowBuilds()
        {
            this.ShowBuilds(this.TeamProjectDropDown.Text);
        }

        private void TeamProjectDropDown_SelectedIndexChanged(object sender, EventArgs e)
        {
            Settings.Default.ProjectName = this.TeamProjectDropDown.SelectedItem.ToString();
            this.ShowBuilds();
        }

        private void lblServerUri_Resize(object sender, EventArgs e)
        {
            this.ChangeServerLink.Left = this.lblServerUri.Left + this.lblServerUri.Width + 5;
        }
    }
}