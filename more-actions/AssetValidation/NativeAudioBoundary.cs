// Native decoder boundary only. Real local-file decoding is verified separately.
namespace UnityEngine
{
    public enum AudioType { MPEG }
}
namespace UnityEngine.Networking
{
    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public bool isDone,Sent,Disposed,Aborted;
        public Result result=Result.InProgress;
        public string error;
        public UnityEngine.AudioClip Clip;
        public void SendWebRequest(){Boundary.UnityCall();Sent=true;}
        public void Abort(){Boundary.UnityCall();Aborted=true;}
        public void Dispose(){Boundary.UnityCall();Disposed=true;}
    }
    public static class UnityWebRequestMultimedia
    {
        public static Func<string,UnityWebRequest> Factory;
        public static readonly List<(string Uri,UnityEngine.AudioType Type,UnityWebRequest Request)> Calls=new();
        public static UnityWebRequest GetAudioClip(string uri,UnityEngine.AudioType type)
        {
            Boundary.UnityCall();
            var request=Factory(uri);Calls.Add((uri,type,request));return request;
        }
    }
    public static class DownloadHandlerAudioClip
    {
        public static UnityEngine.AudioClip GetContent(UnityWebRequest request){Boundary.UnityCall();return request.Clip;}
    }
}
