using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;

namespace OndertitelLezerUwp.Services
{
    public class TtsSynthesizer
    {
        private SpeechSynthesizer _synthesizer;
        private SpeechSynthesisStream _synthesisStream;
        private List<string> _ocrLinesToSynthesize;
        
        public TtsSynthesizer()
        {
            _synthesizer = new SpeechSynthesizer();
            _ocrLinesToSynthesize = new List<string>();

            //load 'Microsoft Bart' - nl-BE voice by default
            VoiceInformation voiceInfo =
             (
               from voice in SpeechSynthesizer.AllVoices
               where voice.Language == "nl-BE"
               select voice
             ).FirstOrDefault() ?? SpeechSynthesizer.DefaultVoice;
            _synthesizer.Voice = voiceInfo;
            _synthesizer.Options.SpeakingRate = _synthesisSpeakingRate;
            
        }

        public void Reset()
        {
            _ocrLinesToSynthesize = new List<string>();
            
        }

         
        public void AddUtteranceToSynthesize(string text)
        {
            _ocrLinesToSynthesize.Add(text);
        }


        /// <summary>
        /// Leverage SpeechSynthesize
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<SpeechSynthesisStream> GetSynthesizedTextStream()
        {
            //detect if rate has to go up
            if(_ocrLinesToSynthesize.Count > 1)
            { 
                if (_synthesisSpeakingRate < 1.7 && _ocrLinesToSynthesize.Count > 2)
                {
                    _synthesisSpeakingRate += 0.2;
                }
                else
                {
                    _synthesisSpeakingRate = 1.2;
                }
             }

            if (_ocrLinesToSynthesize.Count > 0)
            {
                string currentText = "";
                try
                {
                    // Create a stream from the text. This will be played using a media element.
                    currentText = _ocrLinesToSynthesize.FirstOrDefault();
                    _ocrLinesToSynthesize.RemoveAt(0);

                    _synthesizer.Options.SpeakingRate = _synthesisSpeakingRate;
                    _synthesisStream = await _synthesizer.SynthesizeTextToStreamAsync(currentText);

                    return _synthesisStream;
                }
                catch (System.IndexOutOfRangeException)
                {
                    //throw
                }
                catch (Exception e)
                {
                    // If the text is unable to be synthesized, throw an error message to the user.
                    throw new Exception("Synth player error", e);
                }
            }

            return null;

        }

        private double _synthesisSpeakingRate = 1.2;
        public double SynthesisSpeakingRate
        {
            get { return _synthesisSpeakingRate;  }
            set { _synthesisSpeakingRate = value; }
        }


    }
}
