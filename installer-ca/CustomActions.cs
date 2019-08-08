/*
 * Copyright (C) 2019 ITarian LLC
 *
 * ITarian Squid Installer software is distributed under GPL license.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

using Microsoft.Deployment.WindowsInstaller;

namespace Installer
{
    public class CustomActions
    {
        const string INSTALLFOLDER = "INSTALLFOLDER";
        const string WIXUI_INSTALLDIR_VALID = "WIXUI_INSTALLDIR_VALID";

        const string SQUID_ACCEPT_NETWORKS = "SQUID_ACCEPT_NETWORKS";
        const string SQUID_DNS_SERVERS = "SQUID_DNS_SERVERS";
        const string SQUID_SETTINGS_VALID = "SQUID_SETTINGS_VALID";

        [CustomAction]
        public static ActionResult ValidateInstallPath(Session session)
        {
            session.Log("Begin ValidateInstallPath");
            try
            {
                string install_path = session[INSTALLFOLDER];
                if (install_path.Contains(' '))
                {
                    session.Log("Install path invalid: the path should not contain spaces.");
                    session[WIXUI_INSTALLDIR_VALID] = "0";
                }
            }
            catch (Exception x)
            {
                session.Log("Failed ValidateInstallPath: {0}", x);

                return ActionResult.Failure;
            }

            session.Log("End ValidateInstallPath");

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult ValidateSquidSettings(Session session)
        {
            session.Log("Begin ValidateSquidSettings");
            try
            {
                bool validate()
                {
                    var networks_list = session[SQUID_ACCEPT_NETWORKS].Split(',');
                    if (networks_list.Length == 0)
                        return false;
                    foreach (var ip_prefix in networks_list)
                    {
                        var parts = ip_prefix.Split('/');
                        if (parts.Length != 2)
                            return false;
                        if (!IPAddress.TryParse(parts[0].Trim(), out IPAddress _))
                            return false;
                        if (!Byte.TryParse(parts[1].Trim(), out byte prefix) || (prefix == 0 || prefix > 32))
                            return false;
                    }

                    var host_list = session[SQUID_DNS_SERVERS].Split(',');
                    if (host_list.Length == 0)
                        return false;
                    foreach (var host in host_list)
                    {
                        if (Uri.CheckHostName(host.Trim()) == UriHostNameType.Unknown)
                            return false;
                    }

                    return true;
                };

                if (validate())
                    session[SQUID_SETTINGS_VALID] = "1";
            }
            catch (Exception x)
            {
                session.Log("Failed ValidateSquidSettings: {0}", x);

                return ActionResult.Failure;
            }

            session.Log("End ValidateSquidSettings");

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult ApplySquidSettings(Session session)
        {
            var result = ActionResult.Failure;

            const string LINE_SQUID_LOCALNET = "#LINE_SQUID_LOCALNET";
            const string LINE_SQUID_CACHE_DIR = "#LINE_SQUID_CACHE_DIR";
            const string LINE_SQUID_DNS_SERVERS = "#LINE_SQUID_DNS_SERVERS";

            session.Log("Begin ApplySquidSettings");

            if (session.CustomActionData[SQUID_SETTINGS_VALID] == "1")
            {
                try
                {
                    string install_path = session.CustomActionData[INSTALLFOLDER];
                    string config_path = Path.Combine(install_path, @"etc\squid\squid.conf");
                    var config_lines = File.ReadAllLines(config_path).ToList();
                    for (int i = 0; i < config_lines.Count; ++i)
                    {
                        if (config_lines[i].StartsWith(LINE_SQUID_LOCALNET))
                        {
                            config_lines.RemoveAt(i);
                            var networks_list = session.CustomActionData[SQUID_ACCEPT_NETWORKS].Split(',');
                            foreach (var ip_prefix in networks_list.Reverse())
                            {
                                string value = String.Format("acl localnet src {0}", ip_prefix.Trim());
                                session.Log("Inserting value at line {0}: '{1}'", i, value);
                                config_lines.Insert(i, value);
                            }
                            if (networks_list.Any())
                                i += (networks_list.Length - 1);
                            continue;
                        }
                        if (config_lines[i].StartsWith(LINE_SQUID_CACHE_DIR))
                        {
                            config_lines.RemoveAt(i);
                            string cache_path = "/cygdrive/" + install_path.Replace(":\\", "/").Replace('\\', '/').TrimEnd('/') + "/cache";
                            string value = String.Format("cache_dir aufs {0} 3000 16 256", cache_path);
                            session.Log("Inserting value at line {0}: '{1}'", i, value);
                            config_lines.Insert(i, value);
                            continue;
                        }
                        if (config_lines[i].StartsWith(LINE_SQUID_DNS_SERVERS))
                        {
                            config_lines.RemoveAt(i);
                            string value = "dns_nameservers " + session.CustomActionData[SQUID_DNS_SERVERS].Replace(',', ' ');
                            session.Log("Inserting value at line {0}: '{1}'", i, value);
                            config_lines.Insert(i, value);
                            continue;
                        }
                    }

                    File.WriteAllLines(config_path, config_lines.ToArray(), Encoding.UTF8);

                    result = ActionResult.Success;
                }
                catch (Exception x)
                {
                    session.Log("Failed ApplySquidSettings: {0}", x);
                }
            }
            else
            {
                session.Log("Squid settings are not valid, please re-run installer.");
            }

            session.Log("End ApplySquidSettings");

            return result;
        }
    }
}
