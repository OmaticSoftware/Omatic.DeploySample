namespace OmaticSoftware.UiModel
{

	public partial class OmaticDeployedVersionViewDataFormUIModel
	{

		private void OmaticDeployedVersionViewDataFormUIModel_Loaded(object sender, Blackbaud.AppFx.UIModeling.Core.LoadedEventArgs e)
		{
            VERSIONS.DisplayReadOnly = true;
            SERVERNAME.Value = System.Environment.MachineName;
        }

#region "Event handlers"

		partial void OnCreated()
		{
			this.Loaded += OmaticDeployedVersionViewDataFormUIModel_Loaded;
		}

#endregion

	}

}