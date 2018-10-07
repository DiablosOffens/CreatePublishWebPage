using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Serialization;

namespace CreatePublishWebPage
{
    [StructLayout(LayoutKind.Sequential), ComVisible(false)]
    public struct PageData
    {
        public string PublisherName;
        public string ProductName;
        public string ApplicationVersion;
        public string RuntimeVersion;
        public bool CheckClient;
        public string BypassText;
        public string InstallUrl;
        public string NameLabel;
        public string VersionLabel;
        public string PublisherLabel;
        public string ButtonLabel;
        public string BootstrapperText;
        public string SupportText;
        public string SupportUrl;
        public string HelpText;
        public string HelpUrl;
        [XmlArray]
        [XmlArrayItem("Prerequisite", typeof(string))]
        public string[] Prerequisites;
    }

}
