using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OndertitelLezerUwp.Helpers
{
    public static class TextHelpers
    {
        
        //see https://en.wikibooks.org/wiki/Algorithm_Implementation/Strings/Levenshtein_distance
        public static int DamerauLevenshteinDistance(string string1, string string2)
        {
            if (String.IsNullOrEmpty(string1))
            {
                if (!String.IsNullOrEmpty(string2))
                    return string2.Length;

                return 0;
            }

            if (String.IsNullOrEmpty(string2))
            {
                if (!String.IsNullOrEmpty(string1))
                    return string1.Length;

                return 0;
            }

            int length1 = string1.Length;
            int length2 = string2.Length;

            int[,] d = new int[length1 + 1, length2 + 1];

            int cost, del, ins, sub;

            for (int i = 0; i <= d.GetUpperBound(0); i++)
                d[i, 0] = i;

            for (int i = 0; i <= d.GetUpperBound(1); i++)
                d[0, i] = i;

            for (int i = 1; i <= d.GetUpperBound(0); i++)
            {
                for (int j = 1; j <= d.GetUpperBound(1); j++)
                {
                    if (string1[i - 1] == string2[j - 1])
                        cost = 0;
                    else
                        cost = 1;

                    del = d[i - 1, j] + 1;
                    ins = d[i, j - 1] + 1;
                    sub = d[i - 1, j - 1] + cost;

                    d[i, j] = Math.Min(del, Math.Min(ins, sub));

                    if (i > 1 && j > 1 && string1[i - 1] == string2[j - 2] && string1[i - 2] == string2[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                }
            }

            return d[d.GetUpperBound(0), d.GetUpperBound(1)];
        }

        /// <summary>
        /// Helper string method to replace non valid characters with empty string
        /// </summary>
        /// <param name="strIn"></param>
        /// <returns></returns>
        public static string ClearNonAlphanumeric(string inputString)
        {
            // Replace non-alphanumeric characters except . - ! ? with an empty string
            try
            {
                return Regex.Replace(inputString, @"[^\w\.!?,' ]", "",
                                     RegexOptions.None, TimeSpan.FromSeconds(0.5));

            }
            // If error return empty string
            catch (RegexMatchTimeoutException)
            {
                return String.Empty;
            }
        }

        
        public static int CountAccentedCharacters(string input)
        {
            try
            {
                MatchCollection matches = Regex.Matches(input, @"[àÀâÂäÄáÁéÉèÈêÊëËìÌîÎïÏòÒôÔöÖùÙûÛüÜçÇ’ñŒ•]");
                return matches.Count;
            }
            catch
            {
                return 0;
            }
        }


        //remove accents
        public static string ReplaceAccented(string input)
        {
            string beforeConversion = "àÀâÂäÄáÁéÉèÈêÊëËìÌîÎïÏòÒôÔöÖùÙûÛüÜçÇ’ñ•";
            string afterConversion = "aAaAaAaAeEeEeEeEiIiIiIoOoOoOuUuUuUcC'n ";

            System.Text.StringBuilder sb = new System.Text.StringBuilder(input);

            for (int i = 0; i < beforeConversion.Length; i++)
            {
                char beforeChar = beforeConversion[i];
                char afterChar = afterConversion[i];

                sb.Replace(beforeChar, afterChar);
            }

            return sb.ToString();
        }


        ///Another test based on https://stackoverflow.com/questions/653157/a-better-similarity-ranking-algorithm-for-variable-length-strings
        public static double CompareStrings(string str1, string str2)
        {
            List<string> pairs1 = WordLetterPairs(str1.ToUpper());
            List<string> pairs2 = WordLetterPairs(str2.ToUpper());

            int intersection = 0;
            int union = pairs1.Count + pairs2.Count;

            for (int i = 0; i < pairs1.Count; i++)
            {
                for (int j = 0; j < pairs2.Count; j++)
                {
                    if (pairs1[i] == pairs2[j])
                    {
                        intersection++;
                        pairs2.RemoveAt(j);//Must remove the match to prevent "GGGG" from appearing to match "GG" with 100% success

                        break;
                    }
                }
            }

            double comparison = (2.0 * intersection) / union;

            return comparison;
        }


        /// <summary>
        /// Gets all letter pairs for each
        /// individual word in the string
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static List<string> WordLetterPairs(string str)
        {
            List<string> AllPairs = new List<string>();

            // Tokenize the string and put the tokens/words into an array
            string[] Words = Regex.Split(str, @"\s");

            // For each word
            for (int w = 0; w < Words.Length; w++)
            {
                if (!string.IsNullOrEmpty(Words[w]))
                {
                    // Find the pairs of characters
                    String[] PairsInWord = LetterPairs(Words[w]);

                    for (int p = 0; p < PairsInWord.Length; p++)
                    {
                        AllPairs.Add(PairsInWord[p]);
                    }
                }
            }

            return AllPairs;
        }

        /// <summary>
        /// Generates an array containing every 
        /// two consecutive letters in the input string
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static string[] LetterPairs(string str)
        {
            int numPairs = str.Length - 1;

            string[] pairs = new string[numPairs];

            for (int i = 0; i < numPairs; i++)
            {
                pairs[i] = str.Substring(i, 2);
            }

            return pairs;
        }

    }


}
