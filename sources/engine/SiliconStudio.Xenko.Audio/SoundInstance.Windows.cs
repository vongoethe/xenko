﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
#if SILICONSTUDIO_PLATFORM_WINDOWS && !SILICONSTUDIO_XENKO_SOUND_SDL

using System;
using System.Collections.Generic;
using SharpDX;
using SiliconStudio.Core.Mathematics;
using SharpDX.XAudio2;
using SharpDX.X3DAudio;

namespace SiliconStudio.Xenko.Audio
{
    public partial class SoundInstance
    {
        internal SourceVoice SourceVoice;

        private readonly Dictionary<long, SoundSourceBuffer> samplesProcessing = new Dictionary<long, SoundSourceBuffer>();

        internal void Apply3DImpl(AudioListener listener, AudioEmitter emitter)
        {
            //////////////////////////////////////////////////////////////
            // 1. First let's calculate the parameters to set to the voice
            var inputChannels = Sound.Channels;
            var outputChannels = Sound.MasterVoice.VoiceDetails.InputChannelCount;

            if (inputChannels != 1 || outputChannels != 2)
                throw new AudioSystemInternalException("Error in Apply3DImpl only mono sounds are supposed to be localizable");

            var list = new Listener
                {
                    Position = listener.Position.ToSharpDx(), 
                    Velocity = listener.Velocity.ToSharpDx(),
                    OrientFront = listener.Forward.ToSharpDx(),
                    OrientTop = listener.Up.ToSharpDx()
                };
            var emit = new Emitter
                {
                    Position = emitter.Position.ToSharpDx(),
                    Velocity = emitter.Velocity.ToSharpDx(),
                    DopplerScaler = emitter.DopplerScale,
                    CurveDistanceScaler = emitter.DistanceScale,
                    ChannelRadius = 0f, // Multi-channels localizable sound are considered as source of multiple sounds coming from the same location.
                    ChannelCount = inputChannels
                };
            
            var dspSettings = new DspSettings(inputChannels, outputChannels);

            Sound.AudioEngine.X3DAudio.Calculate(list, emit, CalculateFlags.Matrix | CalculateFlags.LpfDirect, dspSettings);

            /////////////////////////////////////////////////////////////
            // 2. Now let's set the voice parameters to simulate a 3D voice.

            // 2.1 The Doppler effect due to the difference of speed between the emitter and listener
            ComputeDopplerFactor(listener, emitter);
            UpdatePitch();

            // 2.2 The channel attenuations due to the source localization.
            localizationChannelVolumes = new[] { dspSettings.MatrixCoefficients[0], dspSettings.MatrixCoefficients[1] };    // only mono sound can be localized so matrix should be 2*1
            UpdateStereoVolumes();
        }

        internal void CreateVoice(int sampleRate, int channels)
        {
            SourceVoice = new SourceVoice(Sound.AudioEngine.XAudio2, new SharpDX.Multimedia.WaveFormat(sampleRate, 16, channels), VoiceFlags.None, 2f, true); // '2f' -> allow to modify pitch up to one octave, 'true' -> enable callback
            SourceVoice.StreamEnd += Stop;
            SourceVoice.BufferEnd += SourceVoiceOnBufferEnd;
        }

        internal void ExitLoopImpl()
        {
            SourceVoice.ExitLoop();
        }

        internal void LoadBuffer()
        {
            var buffer = new AudioBuffer(new DataPointer(Sound.PreloadedData.Pointer, Sound.PreloadedData.Length * sizeof(short)));

            if (IsLooped)
                buffer.LoopCount = AudioBuffer.LoopInfinite;

            SourceVoice.SubmitSourceBuffer(buffer, null);
        }

        internal void LoadBuffer(SoundSourceBuffer samples, bool eos, int length)
        {
            var lptr = samples.Buffer.Pointer.ToInt64();

            var buffer = new AudioBuffer(new DataPointer(samples.Buffer.Pointer, samples.Length * sizeof(short))) { Context = samples.Buffer.Pointer, Flags = !IsLooped && eos ? BufferFlags.EndOfStream : BufferFlags.None };

            SourceVoice.SubmitSourceBuffer(buffer, null);

            samplesProcessing[lptr] = samples;
        }

        internal void PauseImpl()
        {
            SourceVoice.Stop();
        }

        internal void PlatformSpecificDisposeImpl()
        {
            if (SourceVoice == null)
                return;

            SourceVoice.DestroyVoice();
            SourceVoice.Dispose();
        }

        internal void PlayImpl()
        {
            SourceVoice.Start();
        }

        internal void StopImpl()
        {
            SourceVoice.Stop();
            SourceVoice.FlushSourceBuffers();
        }

        internal void UpdateLooping()
        {
            // Nothing to do here for windows version.
            // All the work is done in LoadBuffer.
        }
        internal void UpdateVolume()
        {
            SourceVoice.SetVolume(Volume);
        }

        private void Reset3DImpl()
        {
            // nothing to do here.
        }

        private void SourceVoiceOnBufferEnd(IntPtr intPtr)
        {
            if (SoundSource == null) return;

            SoundSourceBuffer samples;

            //release this buffer
            if (samplesProcessing.TryGetValue(intPtr.ToInt64(), out samples))
            {
                SoundSource.ReleaseSamples(samples);
            }

            //try to read new buffers
            while (SoundSource.ReadSamples(out samples) && SourceVoice.State.BuffersQueued < SoundSource.NumberOfBuffers)
            {
                LoadBuffer(samples, samples.EndOfStream, samples.Length);
                if (samples.EndOfStream && !IsLooped) break;
            }
        }

        private void UpdatePan()
        {
            UpdateStereoVolumes();
        }

        private void UpdatePitch()
        {
            SourceVoice.SetFrequencyRatio(MathUtil.Clamp((float)Math.Pow(2, Pitch) * dopplerPitchFactor, 0.5f, 2f)); // conversion octave to frequencyRatio
        }

        private void UpdateStereoVolumes()
        {
            var sourceChannelCount = Sound.Channels;

            // then update the volume of each channel
            Single[] matrix;
            if (sourceChannelCount == 1)
            {   // panChannelVolumes and localizationChannelVolumes are both in [0,1] so multiplication too, no clamp is needed
                matrix = new[] { panChannelVolumes[0] * localizationChannelVolumes[0], panChannelVolumes[1] * localizationChannelVolumes[1] };
            }
            else if (sourceChannelCount == 2)
            {
                matrix = new[] { panChannelVolumes[0], 0, 0, panChannelVolumes[1] }; // no localization on stereo sounds.
            }
            else
            {
                throw new AudioSystemInternalException("The sound is not supposed to contain more than 2 channels");
            }

            SourceVoice.SetOutputMatrix(sourceChannelCount, Sound.MasterVoice.VoiceDetails.InputChannelCount, matrix);
        }

        public SoundPlayState PlayState { get; internal set; } = SoundPlayState.Stopped;

        private void PreparePlay()
        {            
        }
    }
}

#endif
