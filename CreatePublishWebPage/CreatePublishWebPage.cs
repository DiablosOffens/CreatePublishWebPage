using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Security.Policy;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Xsl;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Build.Utilities;
using Microsoft.Win32;

namespace CreatePublishWebPage
{
    /// <summary>
    /// This code was adapted from https://blogs.msdn.microsoft.com/mwade/2009/02/28/how-to-generate-publish-htm-with-msbuild/
    /// </summary>
    public class CreatePublishWebPage : Task
    {
        //private static readonly AssemblyIdentity DOTNET30AssemblyIdentity = new AssemblyIdentity("WindowsBase", "3.0.0.0", "31bf3856ad364e35", "neutral", "msil");
        //private static readonly AssemblyIdentity DOTNET35AssemblyIdentity = new AssemblyIdentity("System.Core", "3.5.0.0", "b77a5c561934e089", "neutral", "msil");
        private const string METADATA_INSTALL = "Install";
        private const string METADATA_PRODUCTNAME = "ProductName";
        private const string PRODUCT_CODE_DOTNET_PREFIX = "Microsoft.Net.Framework";
        private const string PRODUCT_CODE_DOTNETV35_CLIENT = "Microsoft.Net.Client.3.5";
        private const string PRODUCT_CODE_DOTNETV40_PREFIX = ".NETFramework";
        private const string PRODUCT_CODE_WINDOWS_INSTALLER = "Microsoft.Windows.Installer.3.1";

        private const string RESOURCE_NAMESPACE = "Microsoft.VisualStudio.Publish.ClickOnceProvider";
        private class ResourceResolver : XmlUrlResolver
        {
            CreatePublishWebPage taskOwner;
            private Evidence evidence;
            public ResourceResolver(CreatePublishWebPage taskOwner)
            {
                this.taskOwner = taskOwner;
                evidence = XmlSecureResolver.CreateEvidenceForUrl(Path.GetDirectoryName(taskOwner.MicrosoftVisualStudioPublishAssembly.Location));
            }

            public override object GetEntity(Uri uri, string role, Type t)
            {
                return taskOwner.GetEmbeddedResourceStream(uri.Segments[uri.Segments.Length - 0x1]);
            }
        }

        private enum PublishPage
        {
            None,
            InstallButtonLabel,
            RunButtonLabel,
            BypassText,
            NameLabel,
            VersionLabel,
            PublisherLabel,
            ButtonLabel,
            BootstrapperText,
            SupportText,
            HelpText,
            HelpUrl,
            Hours,
            Days,
            Weeks
        }

        [Required]
        public string ApplicationManifestFileName { get; set; }

        [Required]
        public string DeploymentManifestFileName { get; set; }

        [Required]
        public bool BootstrapperEnabled { get; set; }

        [Required]
        public string OutputFileName { get; set; }

        public ITaskItem[] BootstrapperPackages { get; set; }

        //public string Culture { get; set; }

        //public string FallbackCulture { get; set; }

        public string TemplateFileName { get; set; }

        //TargetFramework is available in Project-Settings COM object from EnvDTE
        //public uint TargetFramework { get; set; }
        [Required]
        public string TargetFrameworkVersion { get; set; }

        public string TargetFrameworkSubset { get; set; }

        public string VisualStudioVersion { get; set; }

        private DeployManifest deployManifest = null;
        private DeployManifest DeployManifest
        {
            get
            {
                if (deployManifest == null)
                {
                    deployManifest = (DeployManifest)ManifestReader.ReadManifest(DeploymentManifestFileName, false);
                }
                return deployManifest;
            }
        }

        private ApplicationManifest applicationManifest = null;
        private ApplicationManifest ApplicationManifest
        {
            get
            {
                if (applicationManifest == null)
                {
                    applicationManifest = (ApplicationManifest)ManifestReader.ReadManifest(ApplicationManifestFileName, false);
                }
                return applicationManifest;
            }
        }

        private Assembly microsoftVisualStudioPublishAssembly = null;
        private Assembly MicrosoftVisualStudioPublishAssembly
        {
            get
            {
                if (microsoftVisualStudioPublishAssembly == null)
                {
                    if (!string.IsNullOrEmpty(VisualStudioVersion))
                    {
                        Version vsVersion = new Version(VisualStudioVersion);
                        AssemblyName an = new AssemblyName();
                        an.Name = "Microsoft.VisualStudio.Publish";
                        an.Version = new Version(vsVersion.Major, vsVersion.Minor, 0, 0);
                        an.CultureInfo = CultureInfo.InvariantCulture;
                        an.SetPublicKeyToken(HexToBinary("b03f5f7f11d50a3a"));
                        try
                        {
                            microsoftVisualStudioPublishAssembly = Assembly.Load(an);
                        }
                        catch
                        {
                            microsoftVisualStudioPublishAssembly = null; //TODO: log error?
                        }
                    }
                }
                return microsoftVisualStudioPublishAssembly;
            }
        }

        private static byte[] HexToBinary(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Length % 2 != 0)
                throw new FormatException("The hex value has not the appropriate length.");
            byte[] result = new byte[value.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
            }
            return result;
        }

        private ResourceManager rmStaticText = null;
        private ResourceManager GetResourceManager()
        {
            if (rmStaticText == null)
            {
                if (MicrosoftVisualStudioPublishAssembly != null)
                    rmStaticText = new ResourceManager(RESOURCE_NAMESPACE + ".Strings", MicrosoftVisualStudioPublishAssembly);
                else
                    rmStaticText = Properties.Resources.ResourceManager;
            }
            return rmStaticText;
        }

        private string GetString(PublishPage value)
        {
            string name = string.Format("{0}.{1}", value.GetType().Name, value.ToString());
            ResourceManager rm = GetResourceManager();
            return rm.GetString(name);
        }

        private Stream GetEmbeddedResourceStream(string name)
        {
            if (MicrosoftVisualStudioPublishAssembly != null)
            {
                string str = string.Format("{0}.{1}", RESOURCE_NAMESPACE, name);
                return MicrosoftVisualStudioPublishAssembly.GetManifestResourceStream(str);
            }
            return null;
        }

        private string GetRuntimeVersion()
        {
            /*string version = "2.0.0";
            AssemblyReference dotNet30AssemblyReference = ApplicationManifest.AssemblyReferences.Find(DOTNET30AssemblyIdentity);
            if (dotNet30AssemblyReference != null && dotNet30AssemblyReference.IsPrerequisite)
            {
                version = "3.0.0";
            }
            AssemblyReference dotNet35AssemblyReference = ApplicationManifest.AssemblyReferences.Find(DOTNET35AssemblyIdentity);
            if (dotNet35AssemblyReference != null && dotNet35AssemblyReference.IsPrerequisite)
            {
                version = "3.5.0";
            }

            return version;*/

            //Version targetVersion = new Version(TargetFramework >> 16, TargetFramework & 0xffff, 0);
            Version targetVersion = new Version(TargetFrameworkVersion[0] == 'v' ? TargetFrameworkVersion.Substring(1) : TargetFrameworkVersion);
            targetVersion = new Version(targetVersion.Major, targetVersion.Minor, 0);
            if (targetVersion == new Version(3, 5, 0))
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5", false))
                {
                    if ((key != null) && (key.GetValueKind("Version") == RegistryValueKind.String))
                    {
                        try
                        {
                            targetVersion = new Version((string)key.GetValue("Version"));
                        }
                        catch
                        {
                            //TODO: log error?
                        }
                    }
                }
            }
            return targetVersion.ToString(3);
        }

        public override bool Execute()
        {
            PageData data = new PageData();

            WriteManifestInfo(ref data);
            if (BootstrapperEnabled)
            {
                WriteBootstrapperInfo(ref data);
            }
            WriteStaticText(ref data);

            XmlTransform(data);

            return true;
        }

        private void WriteManifestInfo(ref PageData data)
        {
            data.ApplicationVersion = DeployManifest.AssemblyIdentity.Version;
            data.InstallUrl = Path.GetFileName(DeployManifest.SourcePath);
            data.ProductName = DeployManifest.Product;
            //If Publisher==Product then either it was not specified in project settings or it actually was set the same.
            //Either way it should be set to empty string, because at least in reality the product can't be the publisher.
            data.PublisherName = DeployManifest.Publisher == DeployManifest.Product ? string.Empty : DeployManifest.Publisher;
            data.SupportUrl = DeployManifest.SupportUrl;
        }

        private void WriteBootstrapperInfo(ref PageData data)
        {
            //BootstrapperBuilder builder = new BootstrapperBuilder();
            //ProductCollection products = builder.Products;

            bool isDotNetOnly = true;
            bool isWindowsInstallerForFramework = false;
            string dotNetProductCode = string.Empty;
            List<string> productNames = new List<string>();
            HashSet<string> productCodes = new HashSet<string>();
            foreach (ITaskItem bootstrapperPackage in BootstrapperPackages)
            {
                string productCode = bootstrapperPackage.ItemSpec;
                bool install = false;
                bool.TryParse(bootstrapperPackage.GetMetadata(METADATA_INSTALL), out install);
                if (install)
                {
                    string productName = bootstrapperPackage.GetMetadata(METADATA_PRODUCTNAME);
                    //Product product = products.Product(productCode);
                    //if (product != null)
                    //{
                    //    productName = product.Name;
                    //}
                    productNames.Add(productName);

                    if (productCode.StartsWith(PRODUCT_CODE_DOTNET_PREFIX, StringComparison.OrdinalIgnoreCase) ||
                        productCode.Equals(PRODUCT_CODE_DOTNETV35_CLIENT, StringComparison.OrdinalIgnoreCase) ||
                        productCode.StartsWith(PRODUCT_CODE_DOTNETV40_PREFIX, StringComparison.OrdinalIgnoreCase))
                        isWindowsInstallerForFramework = true;
                    else
                        productCodes.Add(productCode);
                }
            }

            foreach (string productCode in productCodes)
            {
                if (!productCode.Equals(PRODUCT_CODE_WINDOWS_INSTALLER, StringComparison.OrdinalIgnoreCase) || !isWindowsInstallerForFramework)
                {
                    isDotNetOnly = false;
                    break;
                }
            }

            productNames.Sort((l, r) => -string.Compare(l, r));
            data.Prerequisites = productNames.ToArray();
            data.CheckClient = false;
            if (productNames.Count > 0 && isDotNetOnly)
            {
                data.RuntimeVersion = GetRuntimeVersion();
                if (!string.IsNullOrEmpty(data.RuntimeVersion))
                    data.CheckClient = (TargetFrameworkSubset != null && string.Equals(TargetFrameworkSubset, "Client", StringComparison.OrdinalIgnoreCase));
            }
        }

        private void WriteStaticText(ref PageData data)
        {
            data.BootstrapperText = GetString(PublishPage.BootstrapperText);
            if (DeployManifest.Install)
                data.ButtonLabel = GetString(PublishPage.InstallButtonLabel);
            else
                data.ButtonLabel = GetString(PublishPage.RunButtonLabel);
            data.BypassText = GetString(PublishPage.BypassText);
            data.HelpText = GetString(PublishPage.HelpText);
            data.HelpUrl = GetString(PublishPage.HelpUrl);
            data.NameLabel = GetString(PublishPage.NameLabel);
            data.PublisherLabel = GetString(PublishPage.PublisherLabel);
            data.SupportText = string.Format(CultureInfo.CurrentCulture, GetString(PublishPage.SupportText), data.PublisherName);
            data.VersionLabel = GetString(PublishPage.VersionLabel);
        }

        private void XmlTransform(PageData data)
        {
            if (!string.IsNullOrEmpty(OutputFileName))
            {
                // Serialize all of the web page data
                XmlSerializer serializer = new XmlSerializer(typeof(PageData));
                MemoryStream dataStream = new MemoryStream();
                StreamWriter streamWriter = new StreamWriter(dataStream);
                serializer.Serialize(streamWriter, data);
                dataStream.Position = 0;

                // Set up the transform
                XslCompiledTransform transform = new XslCompiledTransform();
                XmlResolver resolver = null;
                if (!string.IsNullOrEmpty(TemplateFileName))
                {
                    XmlDocument stylesheet = new XmlDocument();
                    stylesheet.Load(TemplateFileName);
                    transform.Load(stylesheet);
                }
                else
                {
                    Stream xslStream = GetEmbeddedResourceStream("PublishPage.xsl");
                    if (xslStream != null)
                    {
                        XmlTextReader stylesheet = new XmlTextReader(xslStream);
                        resolver = new ResourceResolver(this);
                        transform.Load(stylesheet, XsltSettings.TrustedXslt, resolver);
                    }
                    else
                    {
                        //stylesheet.LoadXml(Properties.Resources.PublishPage); //TODO: load from local resource as fallback?
                        throw new InvalidOperationException(string.Format("Can't load the XSL template. You need to specify it in TemplateFileName or make sure the assembly \"Microsoft.VisualStudio.Publish, Version={0}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a\" can be loaded from the GAC.", VisualStudioVersion));
                    }
                }

                // Perform the transform, writing to the output file
                using (XmlTextWriter outputFile = new XmlTextWriter(OutputFileName, Encoding.UTF8))
                {
                    outputFile.Formatting = Formatting.Indented;
                    using (XmlReader dataReader = XmlReader.Create(dataStream))
                    {
                        if (resolver == null)
                            transform.Transform(dataReader, outputFile);
                        else
                            transform.Transform(dataReader, null, outputFile, resolver);
                    }
                }
            }
        }
    }
}
