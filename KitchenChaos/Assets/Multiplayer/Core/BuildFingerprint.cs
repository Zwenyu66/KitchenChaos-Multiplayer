using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KitchenChaos.Multiplayer {
    public static class BuildFingerprint {
        public static string Normalize(string text) {return text.Replace("\r\n","\n").Replace("\r","\n");}
        public static string Compute(IEnumerable<KeyValuePair<string,string>> files) {
            var normalized=files.Select(f=>new KeyValuePair<string,string>(f.Key.Replace('\\','/'),Normalize(f.Value)));
            string input="KitchenChaos-Mirror-v2|Mirror-96.11.5\n"+string.Join("\n",normalized.OrderBy(f=>f.Key,StringComparer.Ordinal).Select(f=>f.Key+"\n"+f.Value));
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-","");
        }
    }
}
