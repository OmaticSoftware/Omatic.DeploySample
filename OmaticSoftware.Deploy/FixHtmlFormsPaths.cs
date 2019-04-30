using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace OmaticSoftware.Deploy
{
    public class FixHtmlFormsPaths : Task
    {

        private List<ITaskItem> _fixedPaths;
        [Required()]
        public ITaskItem[] Paths { get; set; }

        [Required()]
        public string DestinationFolder { get; set; }

        [Output()]
        public ITaskItem[] FixedPaths
        {
            get { return _fixedPaths.ToArray(); }
        }

        public override bool Execute()
        {
            _fixedPaths = new List<ITaskItem>();

            Log.LogMessage("Fixing HTML forms file paths");

            Regex @remove = new Regex("^.*\\\\htmlforms\\\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (string path in Paths.Select(x => x.ItemSpec))
            {
                string newPath = System.IO.Path.Combine(DestinationFolder, @remove.Replace(path, ""));
                Log.LogMessage(MessageImportance.Low, "Old path: \"{0}\"\tNew path: \"{1}\"", path, newPath);
                _fixedPaths.Add(new TaskItem(newPath));
            }
            return true;
        }
    }
}
