using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SFM.Actions;

// Native decoding runs asynchronously; all Unity API calls stay on the main
// thread. The cache takes clip ownership only after full preparation succeeds.
internal sealed class NativeAudioLoad : IDisposable
{
    readonly string path;
    UnityWebRequest request;
    AudioClip clip;
    bool disposed, complete;

    internal NativeAudioLoad(string path) { this.path=Path.GetFullPath(path); }
    internal static bool Supports(string path) => string.Equals(Path.GetExtension(path),".mp3",StringComparison.OrdinalIgnoreCase);

    internal bool Step()
    {
        if(disposed)throw new ObjectDisposedException(nameof(NativeAudioLoad));
        if(complete)return true;
        if(request==null)
        {
            request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.MPEG);
            if(request==null)throw new InvalidOperationException("Native MP3 request could not be created");
            request.SendWebRequest();
            return false;
        }
        if(!request.isDone)return false;
        if(request.result!=UnityWebRequest.Result.Success)
            throw new InvalidDataException("MP3 decoding failed: "+request.error);
        if(clip==null)
        {
            clip=DownloadHandlerAudioClip.GetContent(request);
            if(clip==null)throw new InvalidDataException("MP3 decoder returned no audio clip");
            clip.name=Path.GetFileNameWithoutExtension(path);
            clip.hideFlags=HideFlags.HideAndDontSave;
        }
        if(clip.loadState==AudioDataLoadState.Failed)throw new InvalidDataException("MP3 audio data failed to load");
        if(clip.loadState==AudioDataLoadState.Unloaded&&!clip.LoadAudioData())throw new InvalidDataException("MP3 audio data could not start loading");
        if(clip.loadState!=AudioDataLoadState.Loaded)return false;
        if(!float.IsFinite(clip.length)||clip.length<=0)throw new InvalidDataException("MP3 contains no playable audio");
        request.Dispose();request=null;complete=true;
        return true;
    }

    internal AudioClip TakeClip()
    {
        if(!complete||clip==null)throw new InvalidOperationException("MP3 audio is not ready");
        var result=clip;clip=null;return result;
    }

    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        try { if(request!=null&&!request.isDone)request.Abort(); }
        finally
        {
            try { request?.Dispose();request=null; }
            finally { if(clip!=null)UnityEngine.Object.Destroy(clip);clip=null; }
        }
    }
}
