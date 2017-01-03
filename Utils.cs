using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace TransformiceSWFExtractor
{
    static class Utils
    {
        public static void Each<T>(this IEnumerable<T> ie, Action<T, int> action)
        {
            var i = 0;
            foreach (var e in ie) action(e, i++);
        }

        public static String findStringByParam(String param, String input)
        {
            var regex = new Regex(Regex.Escape(param) + @":<q>\[public\]::(.*?) = (.*?)\r\n");
            var match = regex.Match(input);

            return match.Success ? match.Groups[2].Value : null;
        }

        public static int findIntByParam(String param, String input)
        {
            String strvalue = findStringByParam(param, input);
            strvalue = strvalue.Split('.')[0];
            int intvalue;
            return int.TryParse(strvalue, out intvalue) ? intvalue : -1;
        }

        public static int getFunctionValueInt(String functionName, List<String> input)
        {
            // todo: search with params number
            var regex = new Regex(Regex.Escape(functionName) + @"=\(\)\(0 params, 0 optional\)", RegexOptions.Compiled);
            var declarationRegex = new Regex(@"push(byte|short|int) (-|)(\d+)$", RegexOptions.Compiled);
            int output = 0;

            input.Each((line, n) =>
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    // function found
                    for (int i = 0; i < 20; i++)
                    {
                        // search for values
                        var thisline = input[n + i];
                        if (thisline.Contains("returnvalue")) {
                            break;
                        }
                        var declarationMatch = declarationRegex.Match(thisline);
                        if (declarationMatch.Success)
                        {
                            output += int.Parse(declarationMatch.Groups[2].Value + declarationMatch.Groups[3].Value);
                        }
                    }
                }
            });

            return output;
        }
    }
}
