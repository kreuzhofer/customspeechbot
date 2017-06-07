using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using System.Linq;
using System.Net.Http;
using VideoToTextEchoBot.Services;
using System.IO;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Web;

namespace VideoToTextEchoBot.Dialogs
{
    [Serializable]
    public class RootDialog : IDialog<object>
    {
        private readonly CustomSpeechService speechService = new CustomSpeechService();

        public Task StartAsync(IDialogContext context)
        {
            context.Wait(MessageReceivedAsync);

            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<object> result)
        {
            var activity = await result as Activity;

            if (activity.Attachments.Any())
            {
                Trace.WriteLine(activity.Attachments.FirstOrDefault().ContentType);
            }

            var audioAttachment = activity.Attachments?.FirstOrDefault(a => a.ContentType.StartsWith("video"));
            if (audioAttachment != null)
            {
                await context.PostAsync($"Processing your video upload...");
                var connector = new ConnectorClient(new Uri(activity.ServiceUrl));

                Debug.WriteLine(audioAttachment.ContentType);
                string fileExtension = "";
                if (audioAttachment.ContentType.EndsWith("/quicktime"))
                {
                    fileExtension = ".mov";
                }
                else if (audioAttachment.ContentType.EndsWith("mp4"))
                {
                    fileExtension = ".mp4";
                }
                else
                {
                    fileExtension = ".mp4";
                }

                var bytes = await GetAudioBytesAsync(connector, audioAttachment);
                Trace.WriteLine("Downloaded " + bytes.Length + " bytes.");
                var tempPath = Path.GetTempPath();
                var tempVideoPath = Path.Combine(tempPath, "temp" + fileExtension);
                var tempWavPath = Path.Combine(tempPath, "temp.wav");
                Trace.WriteLine(tempVideoPath);
                Trace.WriteLine(tempWavPath);
                File.WriteAllBytes(tempVideoPath, bytes);
                Trace.WriteLine("File saved to temp folder");
                try
                {
                    using (var process = new Process())
                    {
                        process.StartInfo.FileName = Path.Combine(HttpContext.Current.Server.MapPath("~"), @"ffmpeg\ffmpeg.exe");
                        process.StartInfo.Arguments = @"-i " + tempVideoPath + " " + tempWavPath;
                        process.StartInfo.UseShellExecute = false;
                        process.Start();
                        process.WaitForExit();
                        Trace.WriteLine("WAV file created.");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                    throw;
                }
                string text = string.Empty;
                using (var fileStream = File.OpenRead(tempWavPath))
                {
                    text = await this.speechService.GetTextFromAudioAsync(fileStream);
                    fileStream.Close();
                }
                File.Delete(tempVideoPath);
                File.Delete(tempWavPath);
                int speechLength = (text ?? string.Empty).Length;
                await context.PostAsync($"You said {text} which was {speechLength} characters");
            }
            else
            {
                // calculate something for us to return
                int length = (activity.Text ?? string.Empty).Length;

                Debug.WriteLine(activity.Text);
                System.Diagnostics.Trace.WriteLine(activity.Text);
                // return our reply to the user
                await context.PostAsync($"You sent {activity.Text} which was {length} characters");
            }

            context.Wait(MessageReceivedAsync);
        }

        private static async Task<byte[]> GetAudioBytesAsync(ConnectorClient connector, Attachment audioAttachment)
        {
            Trace.WriteLine(audioAttachment.ContentUrl);
            using (var httpClient = new HttpClient())
            {
                // The Skype attachment URLs are secured by JwtToken,
                // you should set the JwtToken of your bot as the authorization header for the GET request your bot initiates to fetch the image.
                // https://github.com/Microsoft/BotBuilder/issues/662
                var uri = new Uri(audioAttachment.ContentUrl);
                if (uri.Host.EndsWith("skype.com") && uri.Scheme == "https")
                {
                    var token = await GetTokenAsync(connector);
                    Trace.WriteLine(token);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                }

                return await httpClient.GetByteArrayAsync(uri);
            }
        }

        /// <summary>
        /// Gets the JwT token of the bot. 
        /// </summary>
        /// <param name="connector"></param>
        /// <returns>JwT token of the bot</returns>
        private static async Task<string> GetTokenAsync(ConnectorClient connector)
        {
            var credentials = connector.Credentials as MicrosoftAppCredentials;
            if (credentials != null)
            {
                return await credentials.GetTokenAsync();
            }

            return null;
        }
    }
}