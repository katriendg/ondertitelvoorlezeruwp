using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OndertitelLezerUwp.Services
{
    class OcrDectection
    {        
        private Language _ocrLanguage;
        private OcrEngine _ocrEngine;
        private CachedOcrResultSet _cachedResultSet;
        private int _cachedResultSetLoop = 0;

        private string _previousOcrResultString = "0";
        private OcrResult _previousOrcResult = null;
        private bool _isPerformingOcr = false;
           

        public OcrDectection()
        {
            _ocrLanguage = new Language("nl");
            if (!OcrEngine.IsLanguageSupported(_ocrLanguage))
            {
                throw new Exception($"OCR language {_ocrLanguage} not supported.");
            }

            _ocrEngine = OcrEngine.TryCreateFromLanguage(_ocrLanguage);
        }

        public OcrDectection(string language)
        {
            _ocrLanguage = new Language(language);

            _cachedResultSet = new CachedOcrResultSet();
            _cachedResultSetLoop = 0;
        }

        public void Reset()
        {
            _cachedResultSet = new CachedOcrResultSet();
            _cachedResultSetLoop = 0;
        }
        
        public async Task<Tuple<string,int>> PerformOcr(SoftwareBitmap bitmap, int confidenceThreshold, bool isOneThirdCapture, int oneThirdTopHeight)
        {
            string returnValue = "";
            int confidenceValue = 0;
            int confidence = 100; //100 is max, start decreasing

            if (_isPerformingOcr)
            {
                //skip if still processing previous
                return new Tuple<string, int>("", 0);
            }

            _isPerformingOcr = true;

            Log.Information($"OCR PerformOcr _isPerformingOcr {_isPerformingOcr}");

            //if this is the first iteration: init all settings, if not check if loop for same sentence is => 5 (= seconds)
            // then speak out otherwise it takes too long
            if (_cachedResultSetLoop > 4 && _cachedResultSet !=null && !_cachedResultSet.SpokenOut)
            {
                Log.Information($"OCR _cachedResultSetLoop > 5 - assign before continuing");
                int maxConf = _cachedResultSet.Collection.Max(x => x.Confidence);
                DetectedOcrString item = _cachedResultSet.Collection.FirstOrDefault(x => x.Confidence == maxConf);

                if (item != null)
                {
                    Log.Information($"OCR _cachedResultSetLoop > 5 - assign before continuing confidence {item.Confidence}");

                    _cachedResultSet.SpokenOut = true;
                    returnValue = item.Sentence;
                    confidenceValue = item.Confidence;
                }
            }

            OcrResult ocrResult = await _ocrEngine.RecognizeAsync(bitmap);

            if (ocrResult == null)
            {
                _isPerformingOcr = false;
                Log.Information($"OCR null.");

                //call take care of current cachedResultSet synthesizing if any in cachedResultset
                if (_cachedResultSet.Collection.Count > 0)
                {
                    //select the most confident and set back to 0
                    int maxConf = _cachedResultSet.Collection.Max(x => x.Confidence);
                    DetectedOcrString item = _cachedResultSet.Collection.FirstOrDefault(x => x.Confidence == maxConf);

                    if (item != null)
                    {
                        returnValue = item.Sentence;
                        confidenceValue = item.Confidence;
                    }
                    //reset for loop
                    _cachedResultSet = new CachedOcrResultSet();
                    _cachedResultSetLoop = 0;

                }

                return new Tuple<string, int>(returnValue, confidenceValue);
            }

            var resultTextString = "";
            var ocrResultTextString = "";
            int totalWords = 0;
            
            if (ocrResult.Lines.Count > 0)
            {
                Log.Information($"OcrDetection - Raw text: {ocrResult.Text}");
                double left = bitmap.PixelWidth; //X
                double top = bitmap.PixelHeight; //Y
                double topOfLine = 0;
                Rect tempRect = new Rect();
                
                foreach (var line in ocrResult.Lines)
                {
                    topOfLine = bitmap.PixelHeight;
                    foreach (var word in line.Words)
                    {
                        left = (word.BoundingRect.Left < left) ? word.BoundingRect.Left : left;
                        top = (word.BoundingRect.Top < top) ? word.BoundingRect.Top : top;
                        if (totalWords == 0)
                        {
                            tempRect = new Rect(word.BoundingRect.Left, word.BoundingRect.Top, word.BoundingRect.Width, word.BoundingRect.Height);
                        }

                        ocrResultTextString += word.Text + " ";
                        totalWords++;
                        tempRect.Union(word.BoundingRect);
                    }
                }

                Rect boundingRectangle = new Rect(left, top, tempRect.Width, tempRect.Height);
                
                if (ocrResultTextString.Length < 3)
                {
                    confidence = 50;
                    _isPerformingOcr = false;
                    return new Tuple<string, int>("", confidence);
                }

                //if more than 2 lines then lower confidence / note applicable for subtitle reading, N/A if not
                if (ocrResult.Lines.Count > 2) { 
                    confidence -= ocrResult.Lines.Count > 3 ? 10 : 5;
                }

                bool angleTooOff = false;
                if (ocrResult.TextAngle != null)
                {
                    //not holding camera horizontally, let's skip these words
                    angleTooOff = ocrResult.TextAngle < -3 || ocrResult.TextAngle > 3 ? true : false;
                    if (angleTooOff)
                    {
                        
                        //skip everything and and return function
                        _isPerformingOcr = false;
                        return new Tuple<string, int>("", confidence);
                    }

                }

                Tuple<string, int> res = OptimizeResult(ocrResultTextString, confidence, totalWords);
                resultTextString = res.Item1;
                confidence = res.Item2;
                Log.Debug($"OcrDetection - OCR optimized: {resultTextString} Confidence: {confidence}");

               
                //get line height - discard when not checking for subtitles
                if (isOneThirdCapture)
                {
                    int maxLineHeight = (oneThirdTopHeight / 2) - 10;
                    double largestLineHeight = ocrResult.Lines.Max(w => w.Words.Max(l => l.BoundingRect.Height));
                    if (largestLineHeight > maxLineHeight)
                    {
                        //lower confidence
                        confidence -= 15;
                        Log.Debug($"OCR line height largest: {largestLineHeight} / max {maxLineHeight}, new confidence {confidence}");
                    }
                }

                //check ocr is not giving results outside of preferred top zone of frame / this should be removed if getting this to work on non subtitles zones
                if (boundingRectangle.Y + boundingRectangle.Height > oneThirdTopHeight)
                {
                    //lower confidence
                    confidence -= 10;
                    Log.Debug($"OCR rect is outside of top third (third value: {oneThirdTopHeight}) / y+height: {boundingRectangle.Y + boundingRectangle.Height}, new confidence {confidence}");
                }

                if (resultTextString.Trim().Length <= 4)
                {
                    Log.Debug($"OcrDetection - OCR optimized: {resultTextString}, Confidence: {confidence} - skipping length < 4 chars");
                    //skip everything and continue with next frame as if nothing
                    _isPerformingOcr = false;
                    return new Tuple<string, int>("", confidence);
                }

                if (confidence > confidenceThreshold)
                {
                    int difference = Helpers.TextHelpers.DamerauLevenshteinDistance(resultTextString, _previousOcrResultString);
                    double compare = Helpers.TextHelpers.CompareStrings(resultTextString, _previousOcrResultString);
                    Log.Information($"OcrDetection previous difference: {difference} / compare percent: {compare}");

                    DetectedOcrString ocrItem = new DetectedOcrString()
                    { BoundingBox = boundingRectangle, Confidence = confidence, Sentence = resultTextString, WordCount = totalWords };

                    if (_cachedResultSet.Collection.Count < 1)
                    {                        
                        _cachedResultSet.Collection.Add(ocrItem);
                        _cachedResultSetLoop += 1;
                        Log.Debug($"OcrDetection - _cachedResultSetLoop <1, : {_cachedResultSetLoop} ");
                    }
                    else
                    {

                        bool similarBoundingRectangle = IsBoundingRectangleSimilar(boundingRectangle, _cachedResultSet.Collection[_cachedResultSet.Collection.Count - 1].BoundingBox); //true if 3 points match out of four top,left,right,bottom
                        bool identicalLeftOrRight = IsPositionLeftRightIdentical(boundingRectangle, _cachedResultSet.Collection[_cachedResultSet.Collection.Count-1].BoundingBox); //true if either side is equal
                        
                        //if similar stuff add to collection
                        if (difference < 10 || compare > 0.8 || (similarBoundingRectangle && compare > 0.5) || (identicalLeftOrRight && compare > 0.5))
                        {
                            //add to list in any case
                            _cachedResultSet.Collection.Add(ocrItem);
                            _cachedResultSetLoop += 1;
                            Log.Information($"OcrDetection - similar, add to existing - _cachedResultSetLoop {_cachedResultSetLoop} ");

                        }
                        else
                        {
                            //first check if results are not similar 
                            if (_previousOrcResult != null && WordsPositionsSimilarity(ocrResult, _previousOrcResult))
                            {
                                //add to list in any case but lower score
                                if (_previousOcrResultString.Length > resultTextString.Length && _previousOcrResultString.Length - resultTextString.Length > 5)
                                {
                                    ocrItem.Confidence -= 5;
                                    Log.Debug($"OcrDetection - WordsPositionsSimilarity lower confidence by -5 ");
                                }
                                _cachedResultSet.Collection.Add(ocrItem);
                                _cachedResultSetLoop += 1;
                                Log.Debug($"OcrDetection - WordsPositionsSimilarity add to existing - _cachedResultSetLoop {_cachedResultSetLoop} ");
                            }
                            else
                            {
                                //else speak out previous and reset with new 
                                //(if not already spoken - if larger than 4th iteration it was already spoken out)
                                if (!_cachedResultSet.SpokenOut)
                                {
                                    int maxConf = _cachedResultSet.Collection.Max(x => x.Confidence);
                                    DetectedOcrString item = _cachedResultSet.Collection.FirstOrDefault(x => x.Confidence == maxConf);

                                    if (item != null)
                                    {
                                        _cachedResultSet.SpokenOut = true;
                                        returnValue = item.Sentence;
                                        confidenceValue = item.Confidence;
                                    }
                                }

                                //reset for loop
                                _cachedResultSet = new CachedOcrResultSet();
                                _cachedResultSet.Collection.Add(ocrItem);
                                _cachedResultSetLoop = 1;
                                Log.Debug($"OcrDetection - not similar, reset to 1 - _cachedResultSetLoop {_cachedResultSetLoop} ");

                            }   
                        }
                    }

                    _previousOrcResult = ocrResult;
                    _previousOcrResultString = resultTextString;
                }
            }
            else
            {
                if (_cachedResultSet.Collection.Count > 0)
                {
                    DetectedOcrString ocrItem = _cachedResultSet.Collection[_cachedResultSet.Collection.Count - 1];
                    _cachedResultSet.Collection.Add(ocrItem);
                    _cachedResultSetLoop += 1;
                    Log.Debug($"OcrDetection - no OCR lines, keeping last to loop, : {_cachedResultSetLoop} ");
                }
            }

            _isPerformingOcr = false;
            return new Tuple<string, int>(returnValue, confidenceValue);

        }


        private bool WordsPositionsSimilarity(OcrResult currentOcr, OcrResult previousOcr)
        {
            bool similarEnough = false;
            int equalposition = 0;

            List<OcrWord> words = currentOcr.Lines.SelectMany(x => x.Words).ToList();
            List<OcrWord> wordsPrevious = previousOcr.Lines.SelectMany(x => x.Words).ToList();
            for (int i = 0; i < words.Count; i++)
            {
                for (int p = 0; p < wordsPrevious.Count; p++)
                {
                    if (words[i].BoundingRect == wordsPrevious[p].BoundingRect)
                    {
                        equalposition = 3;
                        break;
                    }
                }
                if (equalposition > 2)
                {
                    similarEnough = true;
                    break;
                }
            }

            return similarEnough;
        }


        #region Optimization and confidence of extracted text
        //quickfix with regex... hacks to be replaced with a real optimization function on text/ml/spelling
        private Tuple<string, int> OptimizeResult(string ocrString,  int confidence, int totalWords)
        {
            string returnValue = ocrString;
            int returnConfidence = confidence;

            //match dots or other characters in middle of words typically not used in subtitles - lower confidence
            MatchCollection typicalIssueChars = Regex.Matches(returnValue, @"[;:.?)(/!,*—_]+\w+"); 
            if (typicalIssueChars.Count > 0)
            {
                if (totalWords > 1 && totalWords > typicalIssueChars.Count)
                    returnConfidence -= (totalWords - typicalIssueChars.Count) * 3;
                else
                {
                    returnConfidence -= 20;
                }
                Log.Information($"OcrDetection OptimizeResult dotIssuechars {typicalIssueChars.Count} - confidence {returnConfidence}");
            }
           
            int incorrect = -1;
            returnValue = Helpers.TextHelpers.ClearNonAlphanumeric(ocrString);
            int nonAlphacleared = ocrString.TrimEnd().Count() - returnValue.TrimEnd().Count();
            returnConfidence -= nonAlphacleared;
            if (returnValue.Length < 15 && nonAlphacleared > 1)
                returnConfidence -= 5;
            Log.Information($"OcrDetection OptimizeResult clearNonalphanumeric - confidence {returnConfidence}");

            //if too many strange characters like âàùË remove them and lower confidence
            int countSpecial = Helpers.TextHelpers.CountAccentedCharacters(returnValue);
            if (countSpecial > 0)
            {
                returnValue = Helpers.TextHelpers.ReplaceAccented(returnValue);
                returnConfidence -= countSpecial;
                Log.Information($"OcrDetection OptimizeResult special chars {countSpecial} - confidence {returnConfidence}");
            }

            //Match combinations of capitals and numbers (not at start of word) - lower confidence 
            MatchCollection capitalwords = Regex.Matches(returnValue, @"\w[A-Z-0-9]+");
            if (capitalwords.Count > 0)
            {
                if(totalWords > 1 && totalWords > capitalwords.Count)
                    returnConfidence -= capitalwords.Count * 5;
                else
                {
                    returnConfidence -= 10;
                }
                Log.Information($"OcrDetection OptimizeResult capital words groups {capitalwords.Count} - confidence {returnConfidence}");
            }

            MatchCollection singleChars = Regex.Matches(returnValue, @"\ [a-z-A-Z-0-9]\ ");
            if (singleChars.Count > 0)
            {
                returnConfidence -=  (singleChars.Count*5);
                Log.Information($"OcrDetection OptimizeResult singleChars words groups {singleChars.Count} - confidence {returnConfidence}");
            }

            if ((typicalIssueChars.Count>0 && singleChars.Count>0) || (singleChars.Count>0 && capitalwords.Count>0) || countSpecial>0 && (nonAlphacleared>0 || singleChars.Count>0))
            {
                returnConfidence -= 10;
                Log.Information($"OcrDetection OptimizeResult lowering by 10, both single chars and another issue with sentence. Confidence {returnConfidence}");
            }

            //=====
            //QUICK HACKS - common issue with Ik being recognized as LK - quick hacks for pronunciation by the agent
            if (returnValue.IndexOf("lk ", StringComparison.Ordinal) == 0)
            {
                returnValue = "i" + returnValue.Substring(1, returnValue.Length - 1);
                returnConfidence -= 1;
            }
            returnValue = returnValue.Replace("0f", "of");
            returnValue = returnValue.Replace(" lk ", " Ik ");
            returnValue = returnValue.Replace("'lk ", " Ik ");
            returnValue = returnValue.Replace(" Mr ", " Meneer ");
            returnValue = returnValue.Replace("Mr. ", " Meneer ");
            returnValue = returnValue.Replace("Mr ", " Meneer ");
            if (returnValue.IndexOf("'Mr ", StringComparison.Ordinal) == 0)
            {
                returnValue = "Meneer" + returnValue.Substring(3, returnValue.Length - 3);
                returnConfidence -= 1;
            }
            returnValue = returnValue.Replace("_", "");
            incorrect = returnValue.IndexOf(" nlet ", StringComparison.Ordinal);
            if (incorrect > -1)
            {
                returnConfidence -= 1;
                incorrect = -1;
            }
            returnValue = returnValue.Replace(" nlet ", " niet ");
            //=====

            return new Tuple<string, int>(returnValue, returnConfidence);
        }

        #endregion

        private bool IsBoundingRectangleSimilar(Rect boundingRectangle, Rect boundingBox)
        {
            bool isSimilar = false;

            //width height - allow for lower threshold when height is similar as it's close (one or two lines)
            bool diffHeight = (boundingRectangle.Height > boundingBox.Height) ? ((boundingRectangle.Height - boundingBox.Height) < 2) : ((boundingBox.Height - boundingRectangle.Height) < 2);
            bool diffWidth = (boundingRectangle.Width > boundingBox.Width) ? ((boundingRectangle.Width - boundingBox.Width) < 5) : ((boundingBox.Width - boundingRectangle.Width) < 5);

            if (diffHeight && diffWidth)
                isSimilar = true;

            Log.Information($"OCR - IsBoundingRectangleSimilar Height {diffHeight} - Width {diffWidth}");
            return isSimilar;

        }

        private bool IsPositionLeftRightIdentical(Rect boundingRectangle, Rect boundingBox)
         {
            bool isIdentical = false;
            bool posLeft = (boundingRectangle.Left > boundingBox.Left) ? ((boundingRectangle.Left - boundingBox.Left) < 3) : ((boundingBox.Left - boundingRectangle.Left) < 3);
            bool posRight = (boundingRectangle.Right > boundingBox.Right) ? ((boundingRectangle.Right - boundingBox.Right) < 3) : ((boundingBox.Right - boundingRectangle.Right) < 3);
            Log.Information($"OCR IsPositionLeftRightIdentical left {posLeft} - right {posRight} - bounding right {boundingRectangle.Right} - box right {boundingBox.Right}");

            if (posLeft || posRight)
                isIdentical = true;

            return isIdentical;
        }
    }

    
    public class CachedOcrResultSet
    {
        public bool SpokenOut { get; set; } = false;
        public List<DetectedOcrString> Collection = new List<DetectedOcrString>();
    }

    public class DetectedOcrString
    {
        public int Confidence { get; set; } //100 is max, 0 is min
        public string Sentence { get; set; }
        public int WordCount { get; set; }
        public Rect BoundingBox { get; set; }
    }

}
