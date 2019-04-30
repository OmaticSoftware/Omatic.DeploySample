using Blackbaud.AppFx.Server.AppCatalog;
using Blackbaud.AppFx.XmlTypes.DataForms;
using System;
using System.Collections.Generic;
using System.IO;

namespace OmaticSoftware.Catalog.Administration
{
    public sealed class DeployedVersionViewDataForm : AppViewDataForm
    {
        public DataFormItem[] VERSIONS;

        public override AppViewDataFormLoadResult Load()
        {
            System.Reflection.Assembly execAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            Uri fileUri = new Uri(execAssembly.CodeBase);
            FileInfo execAssemblyFile = new FileInfo(fileUri.LocalPath);
            DirectoryInfo execAssemblyDir = execAssemblyFile.Directory;

            var dllFiles = execAssemblyDir.GetFiles("OmaticSoftware.*.dll");

            List<DataFormItem> versionDfis = new List<DataFormItem>();

            foreach (var dllFileName in dllFiles)
            {
                var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dllFileName.FullName);
                DataFormItem dfi = new DataFormItem(new List<string>( new string[] { "NAME", "VERSION" } ));
                dfi.SetValue("NAME", assemblyName.Name);
                dfi.SetValue("VERSION", assemblyName.Version.ToString());
                dfi.SetValue("DATEMODIFIED", dllFileName.LastWriteTime);
                versionDfis.Add(dfi);
            }

            VERSIONS = versionDfis.ToArray();

            return new AppViewDataFormLoadResult(true);
        }

    }
}