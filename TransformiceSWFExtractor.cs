using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace TransformiceSWFExtractor
{
    public class TransformiceSWF
    {
        private bool Downloading;
        public UriBuilder URL = new UriBuilder();

        public int version;
        public String connectionKey;
        public int[] xorKey;
        public int securityIntKey;

        public TransformiceSWF() : this (true, "www.transformice.com", "Transformice.swf") { }

        public TransformiceSWF(bool downloading, String host) : this (downloading, host, "Transformice.swf") { }

        public TransformiceSWF(bool downloading, String host, String path)
        {
            Downloading = downloading;
            
            URL.Host = host;
            URL.Path = path;

            this.DownloadSWF();
        }

        public TransformiceSWF(byte[] swfData)
        {
            URL.Path = "Transformice.swf";
            File.WriteAllBytes("Transformice.swf", swfData);
            this.DecompressSWF();
        }

        public bool LoadSWF()
        {
            WebClient client = null;
            try
            {
                client = new WebClient();
                client.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
                client.DownloadFile(URL.ToString(), "Transformice.swf");
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception in LoadSWF(): " + e.Message);
            }
            finally
            {
                if (client is IDisposable) ((IDisposable)client).Dispose();
            }
            

            return false;
        }

        public void DownloadSWF()
        {
            // download SWF
            int unixTime = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            
            URL.Query = "n=" + unixTime;
            bool swfExists = File.Exists("Transformice.swf");
            if (Downloading || !swfExists)
            {
                Debug.WriteLine(swfExists ? "Downloading swf... " + URL.ToString() : "SWF doesn't exists... Downloading...");
                LoadSWF();
            }
            else
            {
                Debug.WriteLine("Parsing existing SWF...");
            }

            this.DecompressSWF();
        }

        public void DecompressSWF()
        {
            // parse Transformice.swf's bin order
            // generate .as file
            var swfdumpOutput = "";
            var process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.FileName = "swfdump.exe";
            process.StartInfo.Arguments = String.Format("-a {0}", URL.Path);
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            swfdumpOutput = process.StandardOutput.ReadToEnd();
            List<String> swfdumpLines = swfdumpOutput.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            process.WaitForExit();

            Debug.WriteLine("Transformice.swf.as generated: {0} bytes length", swfdumpOutput.Length);
            if (swfdumpOutput.Length > 500000)
            {
                // already unencrypted
                File.Copy(URL.Path, "Unencrypted.swf", true);
                Debug.WriteLine("{0} was already unencrypted! ({1} bytes of .swf.as generated)", URL.Path, swfdumpOutput.Length);
                return;
            }

            // parse safeSTR findproperty <q>\[(private|public)\](NULL|)::(.*?)\n(?s)(.*?)pushstring \"(.*?)\"
            Match safeStrMatch = Regex.Match(swfdumpOutput, @"findproperty <q>\[(private|public)\](NULL|)::(.*?)\r\n(?s)(.*?)pushstring ""(.*?)""\r\n");
            String safeStrName = safeStrMatch.Groups[3].Value;
            String safeStr = safeStrMatch.Groups[5].Value;
            Debug.WriteLine("Safe str found: {0} -> \"{1}\"", safeStrName, safeStr);

            // parse each function's value method <q>\[public\]::Object <q>\[private\]NULL::(.*?)=\(\)\(0 params, 0 optional\)
            Dictionary<String, String> functionValues = new Dictionary<String, String>();

            var functionRegex = new Regex(@"<q>\[public\]::Object <q>\[private\]NULL::(.*?)=\(\)\(0 params, 0 optional\)", RegexOptions.Compiled);
            var valueRegex = new Regex(@"push(byte|short|int) (\d+)", RegexOptions.Compiled);

            swfdumpLines.Each((newLine, n) =>
            {
                Match fmatch = functionRegex.Match(newLine);
                if (fmatch.Success)
                {
                    String functionName = fmatch.Groups[1].Value;
                    Match fvmatch = valueRegex.Match(swfdumpLines[n + 6]);

                    if (fvmatch.Success)
                    {
                        int functionValueIndex = int.Parse(fvmatch.Groups[2].Value);
                        String functionValue = safeStr[functionValueIndex].ToString();

                        functionValues.Add(functionName, functionValue);
                        // Debug.WriteLine("{0}() -> {1}", functionName, functionValue);
                    }
                }
            });

            // parse ACTUAL code of SWF
            String actualScript = "";
            var functionCallRegex = new Regex(@"callproperty <q>\[(private|public)\](NULL|)::(.*?), 0 params", RegexOptions.Compiled);
            swfdumpLines.Each((newLine, n) =>
            {
                if (newLine.Contains("getlocal_0"))
                {
                    Match functionCallMatch = functionCallRegex.Match(swfdumpLines[n + 1]);
                    if (functionCallMatch.Success)
                    {
                        actualScript += functionValues[functionCallMatch.Groups[3].Value];
                        //if (swfdumpLines[n + 2].Contains("getDefinitionByName"))
                        //{
                        //    actualScript += ".";
                        //}
                        //if (swfdumpLines[n + 4].Contains("construct 0 params"))
                        //{
                        //    actualScript += "\r\n";
                        //}
                    }
                }
            });

            // parse "(?s)exports (\d+) as \"(.*?)_(.*?)\""
            Dictionary<String, int> binaryNames = new Dictionary<String, int>();
            foreach (Match match in Regex.Matches(swfdumpOutput, @"(\s+)exports (\d+) as ""(.*?)_(.*?)""\r\n"))
            {
                GroupCollection groups = match.Groups;
                int pos = int.Parse(groups[2].Value);
                String className = groups[3].Value;
                String paramName = groups[4].Value;

                binaryNames.Add(paramName, pos);
                // Debug.WriteLine("{0} -> {1}_{2}", pos, className, paramName);
            }

            // parse actual bin order
            int[] binaryOrder = new int[binaryNames.Count];
            foreach (Match match in Regex.Matches(actualScript, String.Format("writeBytes({0})", String.Join("|", binaryNames.Keys))))
            {
                binaryOrder[Array.IndexOf(binaryOrder, 0)] = binaryNames[match.Groups[1].Value];
            }

            Debug.WriteLine("Binaries count: {0}; order: [{1}]", binaryOrder.Length, string.Join(", ", binaryOrder));

            // create new swf without encryption
            // extract bins
            process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.FileName = "swfbinexport.exe";
            process.StartInfo.Arguments = URL.Path;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();

            // write them to new swf
            String swfName = Path.GetFileNameWithoutExtension(URL.Path);
            using (FileStream stream = new FileStream("Unencrypted.swf", FileMode.Create))
            {
                foreach (int binNumber in binaryOrder)
                {
                    String binaryName = String.Format("{0}-{1}.bin", swfName, binNumber);
                    using (FileStream fstream = new FileStream(binaryName, FileMode.Open))
                    {
                        fstream.CopyTo(stream);
                    }

                    File.Delete(binaryName);
                }

                Debug.WriteLine("Unencrypted.swf was created successfully! {0} bytes length", stream.Length);
            }
        }

        public void parseData()
        {
            // get pseudocode from swf
            var process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.FileName = "swfdump.exe";
            process.StartInfo.Arguments = "-a Unencrypted.swf";

            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            String swfDump = process.StandardOutput.ReadToEnd();
            List<String> swfDumpLines = swfDump.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            process.WaitForExit();

            // save it
            // File.WriteAllLines("Unencrypted.swf.as", swfDumpLines);
            
            // parse 
            var paramRegex = new Regex(@"getproperty (.*?)::(.*?)$", RegexOptions.Compiled);
            var callRegex = new Regex(@"callproperty <q>\[public\]::(.*?), (\d+) params$", RegexOptions.Compiled);
            String xorKeyName = null;
            int xorCallOffset = 0;

            version = 0;
            connectionKey = "";
            xorKey = new int[20];

            swfDumpLines.Each((line, n) =>
            {
                // version & key
                if (version == 0 && line.Contains("callproperty <q>[public]::x_hash, 1 params"))
                {
                    if (swfDumpLines[n - 1].Contains("getlocal_3"))
                    {
                        // end of function
                        int keyEndLineNumber = 0;
                        for (int i = 1; i <= 50; i++)
                        {
                            if (swfDumpLines[n + 14 + i].Contains("Capabilities"))
                            {
                                keyEndLineNumber = 14 + i;
                                break;
                            }
                        }

                        // version
                        var versionParamMatch = paramRegex.Match(swfDumpLines[n + 7]);
                        if (versionParamMatch.Success)
                        {
                            version = Utils.findIntByParam(versionParamMatch.Groups[2].Value, swfDump);
                        }

                        // connection key
                        for (int i = 10; i <= keyEndLineNumber; i++)
                        {
                            if (i % 3 == 1 || i == 11)
                            {
                                var keyPartMatch = paramRegex.Match(swfDumpLines[n + i]);
                                if (keyPartMatch.Success)
                                {
                                    connectionKey += Utils.findStringByParam(keyPartMatch.Groups[2].Value, swfDump);
                                }
                            }
                        }
                    }
                }

                // xor key name
                if (xorKeyName == null && line.Contains("lshift") && swfDumpLines[n + 1].Contains("getlocal r6"))
                {
                    var xorArrayMatch = paramRegex.Match(swfDumpLines[n + 5]);
                    if (xorArrayMatch.Success)
                    {
                        xorKeyName = xorArrayMatch.Groups[2].Value;
                    }
                    
                }

                // securityInt
                if (line.Contains("getlocal_0") && swfDumpLines[n + 2].Contains("convert_i") && swfDumpLines[n + 3].Contains("setlocal_1"))
                {
                    securityIntKey = 0;
                    
                    for (var i = 0; i <= 100; i++)
                    {
                        var sline = swfDumpLines[n + 3 + i];
                        if (sline.Contains("getlocal_1 "))
                        {
                            if (swfDumpLines[n + 3 + i + 3].Contains("bitxor")) // _loc1_ ^ some.function()
                            {
                                Match callMatch = callRegex.Match(swfDumpLines[n + 3 + i + 2]);
                                if (callMatch.Success && callMatch.Groups[2].Value == "0")
                                {
                                    var intOne = Utils.getFunctionValueInt(callMatch.Groups[1].Value, swfDumpLines);
                                    securityIntKey = securityIntKey ^ intOne;
                                }
                            }

                            if (swfDumpLines[n + 3 + i + 5].Contains("lshift")) // _loc1_ ^ some.function() << some.function()
                            {
                                Match callMatch = callRegex.Match(swfDumpLines[n + 3 + i + 2]);
                                if (callMatch.Success && callMatch.Groups[2].Value == "0")
                                {
                                    var intOne = Utils.getFunctionValueInt(callMatch.Groups[1].Value, swfDumpLines);
                                    callMatch = callRegex.Match(swfDumpLines[n + 3 + i + 4]);
                                    if (callMatch.Success && callMatch.Groups[2].Value == "0")
                                    {
                                        var intTwo = Utils.getFunctionValueInt(callMatch.Groups[1].Value, swfDumpLines);
                                        securityIntKey = securityIntKey ^ intOne << intTwo;
                                    }
                                }
                            }
                        }
                        else if (sline.Contains("returnvalue"))
                        {
                            break;
                        }
                    }
                }
            });

            // need 2nd iteration

            swfDumpLines.Each((line, n) =>
            {

                // xor key.push()
                if (xorKeyName != null && line.Contains(xorKeyName))
                {
                    int offset = swfDumpLines[n + 17].Contains("call 4 params") ? 17 : swfDumpLines[n + 15].Contains("call 4 params") ? 15 : -1;

                    if (offset != -1)
                    {
                        int currentXorKeyOffset = -1;

                        for (var i = 0; i <= 80; i++) {
                            var sline = swfDumpLines[n - i];
                            var slineplus = swfDumpLines[n + i];

                            if (sline.Contains("x_proxySteam"))
                            {
                                currentXorKeyOffset = 4 * 0;
                            }
                            else if (sline.Contains("opaqueBackground"))
                            {
                                currentXorKeyOffset = 4 * 3;
                            }
                            else if (slineplus.Contains("Initialisation"))
                            {
                                currentXorKeyOffset = 4 * 2;
                            }
                            else if (sline.Contains("RESIZE"))
                            {
                                currentXorKeyOffset = 4 * 1;
                            }
                            else if(sline.Contains("flash.display::Loader"))
                            {
                                currentXorKeyOffset = 4 * 4;
                            }
                            else
                            {
                                continue;
                            }
                            break;
                        }

                        if (currentXorKeyOffset == -1)
                        {
                            Debug.WriteLine("!!! XOR KEY ERROR !!!" + xorKeyName);
                            xorKey = new int[20];
                            return;
                        }

                        for (var i = 8; i >= 0; i--)
                        {
                            var sline = swfDumpLines[n + offset - i];
                            var callMatch = callRegex.Match(sline);

                            if (callMatch.Success && callMatch.Groups[2].Value == "0") // check for , 0 params
                            {
                                xorKey[currentXorKeyOffset] = Utils.getFunctionValueInt(callMatch.Groups[1].Value, swfDumpLines);
                                currentXorKeyOffset++;
                            }
                        }
                        xorCallOffset++;
                    }
                }
            });

            Debug.WriteLine("Version: 1.{0}; key: {1}\nxor key: [{2}]\nsecurity int key: {3}.", version, connectionKey, String.Join(", ", xorKey), securityIntKey);
        }
    }
}
