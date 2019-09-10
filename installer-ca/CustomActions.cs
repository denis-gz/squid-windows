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

using CryptSharp;

namespace Installer
{
    public class CustomActions
    {
        const string INSTALLFOLDER = "INSTALLFOLDER";
        const string WIXUI_INSTALLDIR_VALID = "WIXUI_INSTALLDIR_VALID";

        const string SQUID_ACCEPT_NETWORKS = "SQUID_ACCEPT_NETWORKS";
        const string SQUID_DNS_SERVERS = "SQUID_DNS_SERVERS";
        const string SQUID_HTTP_PORT = "SQUID_HTTP_PORT";
        const string SQUID_USE_AUTH = "SQUID_USE_AUTH";
        const string SQUID_AUTH_USER = "SQUID_AUTH_USER";
        const string SQUID_AUTH_PASSWORD = "SQUID_AUTH_PASSWORD";
        const string SquidSettingsValid = "SquidSettingsValid";

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
                        {
                            session.Log("Network format is invalid: '" + ip_prefix + "'");
                            return false;
                        }
                        if (!IPAddress.TryParse(parts[0].Trim(), out IPAddress _))
                        {
                            session.Log("Network address is invalid: '" + parts[0].Trim() + "'");
                            return false;
                        }
                        if (!Byte.TryParse(parts[1].Trim(), out byte prefix) || (prefix == 0 || prefix > 32))
                        {
                            session.Log("Network prefix is invalid: '" + parts[1].Trim() + "'");
                            return false;
                        }
                    }

                    var host_list = session[SQUID_DNS_SERVERS].Split(',');
                    if (host_list.Length == 0)
                    {
                        session.Log("DNS host list is empty");
                        return false;
                    }
                    foreach (var host in host_list)
                    {
                        if (Uri.CheckHostName(host.Trim()) == UriHostNameType.Unknown)
                        {
                            session.Log("Bad DNS host name: '" + host.Trim() + "'");
                            return false;
                        }
                    }

                    var port_param = session[SQUID_HTTP_PORT].Trim();
                    if (!UInt16.TryParse(port_param, out ushort http_port))
                    {
                        session.Log("HTTP port is invalid: '" + port_param + "'");
                        return false;
                    }

                    var use_auth_param = session[SQUID_USE_AUTH].Trim();
                    if (use_auth_param == "1")
                    {
                        var auth_user_param = session[SQUID_AUTH_USER];
                        if (auth_user_param.Trim().Length == 0 || auth_user_param.Length > 255 || auth_user_param.Contains(":"))
                        {
                            session.Log("Proxy user name is not valid (should be upto 255 chars in length and not contain a colon symbol (':'): '" + auth_user_param + "'");
                            return false;
                        }
                        var auth_password_param = session[SQUID_AUTH_PASSWORD];
                        if (auth_password_param.Length == 0 || auth_password_param.Length > 255)
                        {
                            session.Log("Proxy user password is not valid (should be upto 255 chars in length)");
                            return false;
                        }
                    }

                    return true;
                };

                if (validate())
                    session[SquidSettingsValid] = "1";
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
            const string LINE_SQUID_HTTP_PORT = "#LINE_SQUID_HTTP_PORT";
            const string LINE_SQUID_HTTP_ACCESS = "#LINE_SQUID_HTTP_ACCESS";
            const string LINE_SQUID_ACL_AUTH = "#LINE_SQUID_ACL_AUTH";

            session.Log("Begin ApplySquidSettings");

            if (session.CustomActionData[SquidSettingsValid] == "1")
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
                        if (config_lines[i].StartsWith(LINE_SQUID_HTTP_PORT))
                        {
                            config_lines.RemoveAt(i);
                            string value = String.Format("http_port {0}", session.CustomActionData[SQUID_HTTP_PORT]);
                            config_lines.Insert(i, value);
                            continue;
                        }
                        if (config_lines[i].StartsWith(LINE_SQUID_CACHE_DIR))
                        {
                            config_lines.RemoveAt(i);
                            string cache_path = GetCygdrivePath(install_path) + "/var/cache";
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
                        if (config_lines[i].StartsWith(LINE_SQUID_ACL_AUTH))
                        {
                            config_lines.RemoveAt(i);
                            if (session.CustomActionData[SQUID_USE_AUTH] == "1")
                            {
                                string cygdrive_path = GetCygdrivePath(install_path);
                                string ncsa_auth_path = cygdrive_path + "/lib/squid/basic_ncsa_auth.exe";
                                string htpasswd_path = cygdrive_path + "/etc/.htpasswd";
                                string value = String.Format("auth_param basic program \"{0}\" \"{1}\"", ncsa_auth_path, htpasswd_path);
                                config_lines.Insert(i++, value);
                                config_lines.Insert(i, "acl ncsa_users proxy_auth REQUIRED");
                            }
                        }
                        if (config_lines[i].StartsWith(LINE_SQUID_HTTP_ACCESS))
                        {
                            config_lines.RemoveAt(i);
                            if (session.CustomActionData[SQUID_USE_AUTH] == "1")
                            {
                                config_lines.Insert(i, "http_access allow ncsa_users");
                            }
                            else
                            {
                                config_lines.Insert(i, "http_access allow localnet");
                            }
                        }
                    }

                    File.WriteAllLines(config_path, config_lines.ToArray());

                    if (session.CustomActionData[SQUID_USE_AUTH] == "1")
                    {
                        string enc_passwd = Crypter.MD5.Crypt(session.CustomActionData[SQUID_AUTH_PASSWORD], new CrypterOptions
                        {
                            { CrypterOption.Variant, MD5CrypterVariant.Apache }
                        });
                        string entry = String.Format("{0}:{1}", session.CustomActionData[SQUID_AUTH_USER], enc_passwd);
                        string htpasswd_path = Path.Combine(install_path, @"etc\.htpasswd");
                        File.WriteAllText(htpasswd_path, entry);
                    }

                    result = ActionResult.Success;
                }
                catch (Exception x)
                {
                    session.Log("Failed ApplySquidSettings: {0}", x);
                }
            }
            else
            {
                session.Log("Squid settings are not valid, please check parameters and re-run installer.");
            }

            session.Log("End ApplySquidSettings");

            return result;
        }

        private static string GetCygdrivePath(string installPath)
        {
            string result = "/cygdrive/" + installPath.Replace(":\\", "/").Replace('\\', '/').TrimEnd('/');
            return result;
        }
    }
}
