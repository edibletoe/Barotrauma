﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NVorbis;
using OpenTK.Audio.OpenAL;

namespace Subsurface.Sounds
{
    internal static class ALHelper
    {
        public static readonly XRamExtension XRam = new XRamExtension();
        public static readonly EffectsExtension Efx = new EffectsExtension();

        static ALHelper()
        {
            try
            {
                Debug.WriteLine("OpenAL Soft [" + (AL.Get(ALGetString.Version).Contains("SOFT") ? "X" : " ") + "], ");
                Debug.WriteLine("X-RAM [" + (XRam.IsInitialized ? "X" : " ") + "], ");
                Debug.WriteLine("Effect Extensions [" + (Efx.IsInitialized ? "X" : " ") + "]");
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("OpenAL error!", e);
            }

        }

        [Conditional("TRACE")]
        public static void TraceMemoryUsage(Action<string, int, int> logHandler)
        {
            var usedHeap = (double)GC.GetTotalMemory(true);

            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            while (usedHeap >= 1024 && order + 1 < sizes.Length)
            {
                order++;
                usedHeap = usedHeap / 1024;
            }

            //logHandler(String.Format("Total memory : {0:0.###} {1}          ", usedHeap, sizes[order]), 0, 6);
        }

        public static void Check()
        {
#if !DEBUG
            return;
#endif

            ALError error;
            if ((error = AL.GetError()) != ALError.NoError)
            {
                DebugConsole.ThrowError("OpenAL error: "+AL.GetErrorString(error));
            }
        }
    }

    public class OggStream : IDisposable
    {
        public const int DefaultBufferCount = 3;

        internal readonly object stopMutex = new object();
        internal readonly object prepareMutex = new object();

        internal readonly int alSourceId;
        internal readonly int[] alBufferIds;

        readonly int alFilterId;
        readonly Stream underlyingStream;

        internal VorbisReader Reader { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Preparing { get; private set; }

        public int BufferCount { get; private set; }

#if TRACE
        public int logX, logY;
        public Action<string, int, int> LogHandler;
#endif

        public OggStream(string filename, int bufferCount = DefaultBufferCount) : this(File.OpenRead(filename), bufferCount) { }
        public OggStream(Stream stream, int bufferCount = DefaultBufferCount)
        {
            BufferCount = bufferCount;

            alBufferIds = AL.GenBuffers(bufferCount);
            alSourceId = AL.GenSource();

            if (ALHelper.XRam.IsInitialized)
            {
                ALHelper.XRam.SetBufferMode(BufferCount, ref alBufferIds[0], XRamExtension.XRamStorage.Hardware);
                ALHelper.Check();
            }

            if (ALHelper.Efx.IsInitialized)
            {
                alFilterId = ALHelper.Efx.GenFilter();
                ALHelper.Efx.Filter(alFilterId, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);
                ALHelper.Efx.Filter(alFilterId, EfxFilterf.LowpassGain, 1);
                LowPassHFGain = 1;
            }

            underlyingStream = stream;

            Volume = 1;
            IsLooped = true;

        }

        public void Prepare()
        {
            if (Preparing) return;

            var state = AL.GetSourceState(alSourceId);

            lock (stopMutex)
            {
                switch (state)
                {
                    case ALSourceState.Playing:
                    case ALSourceState.Paused:
                        return;

                    case ALSourceState.Stopped:
                        lock (prepareMutex)
                        {
                            Close();
                            Empty();
                        }
                        break;
                }

                if (!Ready)
                {
                    lock (prepareMutex)
                    {
                        Preparing = true;
//#if TRACE
//                        logX = 7;
//                        LogHandler("(*", logX, logY);
//                        logX += 2;
//#endif
                        Open(precache: true);
//#if TRACE
//                        LogHandler(")", logX++, logY);
//#endif
                    }
                }
            }
        }

        public void Play()
        {
            var state = AL.GetSourceState(alSourceId);

            switch (state)
            {
                case ALSourceState.Playing: return;
                case ALSourceState.Paused:
                    Resume();
                    return;
            }

            Prepare();

//#if TRACE
//            LogHandler("{", logX++, logY);
//#endif
            AL.SourcePlay(alSourceId);
            try
            {
                ALHelper.Check();
            }
            catch (Exception e)
            {
                throw new Exception ("AIHElper", e.InnerException);
            }

            Preparing = false;

            OggStreamer.Instance.AddStream(this);
        }

        public void Pause()
        {
            if (AL.GetSourceState(alSourceId) != ALSourceState.Playing)
                return;

            OggStreamer.Instance.RemoveStream(this);
//#if TRACE
//            LogHandler("]", logX++, logY);
//#endif
            AL.SourcePause(alSourceId);
            ALHelper.Check();
        }

        public void Resume()
        {
            if (AL.GetSourceState(alSourceId) != ALSourceState.Paused)
                return;

            OggStreamer.Instance.AddStream(this);
#if TRACE
            LogHandler("[", logX++, logY);
#endif
            AL.SourcePlay(alSourceId);
            ALHelper.Check();
        }

        public void Stop()
        {
            var state = AL.GetSourceState(alSourceId);
            if (state == ALSourceState.Playing || state == ALSourceState.Paused)
            {
//#if TRACE
//                LogHandler("}", logX++, logY);
//#endif
                StopPlayback();
            }

            lock (stopMutex)
            {
                OggStreamer.Instance.RemoveStream(this);
            }
        }

        float lowPassHfGain;
        public float LowPassHFGain
        {
            get { return lowPassHfGain; }
            set
            {
                if (ALHelper.Efx.IsInitialized)
                {
                    ALHelper.Efx.Filter(alFilterId, EfxFilterf.LowpassGainHF, lowPassHfGain = value);
                    ALHelper.Efx.BindFilterToSource(alSourceId, alFilterId);
                    ALHelper.Check();
                }
            }
        }

        float volume;
        public float Volume
        {
            get { return volume; }
            set
            {
                AL.Source(alSourceId, ALSourcef.Gain, volume = value);
                ALHelper.Check();
            }
        }

        public bool IsLooped { get; set; }



        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var state = AL.GetSourceState(alSourceId);
                if (state == ALSourceState.Playing || state == ALSourceState.Paused)
                    StopPlayback();

                lock (prepareMutex)
                {
                    OggStreamer.Instance.RemoveStream(this);

                    if (state != ALSourceState.Initial)
                        Empty();

                    Close();

                    underlyingStream.Dispose();
                }

                AL.DeleteSource(alSourceId);
                AL.DeleteBuffers(alBufferIds);

                if (ALHelper.Efx.IsInitialized)
                    ALHelper.Efx.DeleteFilter(alFilterId);

                ALHelper.Check();
    #if TRACE
                ALHelper.TraceMemoryUsage(LogHandler);
    #endif
            }
        }

        void StopPlayback()
        {
            AL.SourceStop(alSourceId);
            ALHelper.Check();
        }

        void Empty()
        {
            int queued;
            AL.GetSource(alSourceId, ALGetSourcei.BuffersQueued, out queued);
            if (queued > 0)
            {
                try
                {
                    AL.SourceUnqueueBuffers(alSourceId, queued);
                    ALHelper.Check();
                }
                catch (InvalidOperationException)
                {
                    // This is a bug in the OpenAL implementation
                    // Salvage what we can
                    int processed;
                    AL.GetSource(alSourceId, ALGetSourcei.BuffersProcessed, out processed);
                    var salvaged = new int[processed];
                    if (processed > 0)
                    {
                        AL.SourceUnqueueBuffers(alSourceId, processed, salvaged);
                        ALHelper.Check();
                    }

                    // Try turning it off again?
                    AL.SourceStop(alSourceId);
                    ALHelper.Check();

                    Empty();
                }
            }
//#if TRACE
//            logX = 7;
//            LogHandler(new string(Enumerable.Repeat(' ', Console.BufferWidth - 6).ToArray()), logX, logY);
//#endif
        }

        internal void Open(bool precache = false)
        {
            underlyingStream.Seek(0, SeekOrigin.Begin);
            Reader = new VorbisReader(underlyingStream, false);

            if (precache)
            {
                // Fill first buffer synchronously
                OggStreamer.Instance.FillBuffer(this, alBufferIds[0]);
                AL.SourceQueueBuffer(alSourceId, alBufferIds[0]);
                ALHelper.Check();

                // Schedule the others asynchronously
                OggStreamer.Instance.AddStream(this);
            }

            Ready = true;
        }

        internal void Close()
        {
            if (Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }
            Ready = false;
        }
    }

    public class OggStreamer : IDisposable
    {
        const float DefaultUpdateRate = 10;
        const int DefaultBufferSize = 44100;

        static readonly object singletonMutex = new object();

        readonly object iterationMutex = new object();
        readonly object readMutex = new object();

        readonly float[] readSampleBuffer;
        readonly short[] castBuffer;

        readonly HashSet<OggStream> streams = new HashSet<OggStream>();
        readonly List<OggStream> threadLocalStreams = new List<OggStream>(); 

        readonly Thread underlyingThread;
        volatile bool cancelled;

        public float UpdateRate { get; private set; }
        public int BufferSize { get; private set; }

        static OggStreamer instance;
        public static OggStreamer Instance
        {
            get
            {
                lock (singletonMutex)
                {
                    if (instance == null)
                        throw new InvalidOperationException("No instance running");
                    return instance;
                }
            }
            private set { lock (singletonMutex) instance = value; }
        }

        public OggStreamer(int bufferSize = DefaultBufferSize, float updateRate = DefaultUpdateRate)
        {
            lock (singletonMutex)
            {
                if (instance != null)
                    throw new InvalidOperationException("Already running");

                Instance = this;
                underlyingThread = new Thread(EnsureBuffersFilled) { Priority = ThreadPriority.Lowest };
                underlyingThread.Start();
            }

            UpdateRate = updateRate;
            BufferSize = bufferSize;

            readSampleBuffer = new float[bufferSize];
            castBuffer = new short[bufferSize];
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (singletonMutex)
                {
                    Debug.Assert(Instance == this, "Two instances running, somehow...?");

                    cancelled = true;
                    lock (iterationMutex)
                        streams.Clear();

                    Instance = null;
                }
            }
        }
        
        internal bool AddStream(OggStream stream)
        {
            lock (iterationMutex)
                return streams.Add(stream);
        }
        internal bool RemoveStream(OggStream stream)
        {
            lock (iterationMutex) 
                return streams.Remove(stream);
        }

        public bool FillBuffer(OggStream stream, int bufferId)
        {
            int readSamples;
            lock (readMutex)
            {
                readSamples = stream.Reader.ReadSamples(readSampleBuffer, 0, BufferSize);
                CastBuffer(readSampleBuffer, castBuffer, readSamples);
            }
            AL.BufferData(bufferId, stream.Reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16, castBuffer,
                          readSamples * sizeof (short), stream.Reader.SampleRate);
            ALHelper.Check();
//#if TRACE
//            stream.LogHandler(readSamples == BufferSize ? "." : "|", stream.logX++, stream.logY);
//            //ALHelper.TraceMemoryUsage(stream.LogHandler);
//#endif

            return readSamples != BufferSize;
        }
        static void CastBuffer(float[] inBuffer, short[] outBuffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                var temp = (int)(32767f * inBuffer[i]);
                if (temp > short.MaxValue) temp = short.MaxValue;
                else if (temp < short.MinValue) temp = short.MinValue;
                outBuffer[i] = (short)temp;
            }
        }

        void EnsureBuffersFilled()
        {
            while (!cancelled)
            {
                Thread.Sleep((int) (1000 / UpdateRate));
                if (cancelled) break;

                threadLocalStreams.Clear();
                lock (iterationMutex) threadLocalStreams.AddRange(streams);

                foreach (var stream in threadLocalStreams)
                {
                    lock (stream.prepareMutex)
                    {
                        lock (iterationMutex)
                            if (!streams.Contains(stream))
                                continue;

                        bool finished = false;

                        int queued;
                        AL.GetSource(stream.alSourceId, ALGetSourcei.BuffersQueued, out queued);
                        //ALHelper.Check();
                        int processed;
                        AL.GetSource(stream.alSourceId, ALGetSourcei.BuffersProcessed, out processed);
                        //ALHelper.Check();

                        if (processed == 0 && queued == stream.BufferCount) continue;

                        int[] tempBuffers;
                        if (processed > 0)
                            tempBuffers = AL.SourceUnqueueBuffers(stream.alSourceId, processed);
                        else
                            tempBuffers = stream.alBufferIds.Skip(queued).ToArray();

                        for (int i = 0; i < tempBuffers.Length; i++)
                        {
                            finished |= FillBuffer(stream, tempBuffers[i]);

                            if (finished)
                            {
                                if (stream.IsLooped)
                                {
                                    stream.Close();
                                    stream.Open();
                                }
                                else
                                {
                                    streams.Remove(stream);
                                    i = tempBuffers.Length;
                                }
                            }
                        }

                        AL.SourceQueueBuffers(stream.alSourceId, tempBuffers.Length, tempBuffers);
                        //ALHelper.Check();

                        if (finished && !stream.IsLooped)
                            continue;
                    }

                    lock (stream.stopMutex)
                    {
                        if (stream.Preparing) continue;

                        lock (iterationMutex)
                            if (!streams.Contains(stream))
                                continue;

                        var state = AL.GetSourceState(stream.alSourceId);
                        if (state == ALSourceState.Stopped)
                        {
//#if TRACE
//                            stream.LogHandler("!", stream.logX++, stream.logY);
//#endif
                            AL.SourcePlay(stream.alSourceId);
                            ALHelper.Check();
                        }
                    }
                }
            }
        }
    }

}
