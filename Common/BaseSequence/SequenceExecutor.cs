﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Timers;
using System.Threading;
using Vixen.Execution;
using Vixen.Services;
using Vixen.Sys;
using Vixen.Module.Timing;
using Vixen.Module.Media;

namespace BaseSequence {
	public class SequenceExecutor : ISequenceExecutor {
		private System.Timers.Timer _endCheckTimer;
		private SynchronizationContext _syncContext;
		private bool _isRunning;

		public event EventHandler<SequenceStartedEventArgs> SequenceStarted;
		public event EventHandler<SequenceEventArgs> SequenceEnded;
		public event EventHandler<ExecutorMessageEventArgs> Message;
		public event EventHandler<ExecutorMessageEventArgs> Error;

		public SequenceExecutor() {
			_endCheckTimer = new System.Timers.Timer(10);
			_endCheckTimer.Elapsed += _EndCheckTimerElapsed;
			_syncContext = AsyncOperationManager.SynchronizationContext;
		}

		#region Public
		// Because these are calculated values, changing the length of the sequence
		// during execution will not affect the end time.
		public TimeSpan StartTime { get; protected set; }

		public TimeSpan EndTime { get; protected set; }

		public ISequence Sequence { get; set; }

		public bool IsRunning {
			get { return _isRunning; }
			private set {
				_isRunning = value;
				if(_isRunning) {
					OnSequenceStarted(new SequenceStartedEventArgs(Sequence, TimingSource, StartTime, EndTime));
				} else {
					OnSequenceEnded(new SequenceEventArgs(Sequence));
				}
			}
		}

		public bool IsPaused { get; private set; }

		public void Start() {
			Play(TimeSpan.Zero, TimeSpan.MaxValue);
		}

		public void Play(TimeSpan startTime, TimeSpan endTime) {
			_Play(startTime, endTime);
		}

		public void Pause() {
			_Pause();
		}

		public void Resume() {
			_Resume();
		}

		public void Stop() {
			_Stop();
		}

		public IEnumerable<ISequenceFilterNode> SequenceFilters {
			get {
				if(Sequence != null) {
					return Sequence.GetAllSequenceFilters();
				}
				return Enumerable.Empty<ISequenceFilterNode>();
			}
		}

		public string Name {
			get {
				if(Sequence != null) {
					return Sequence.Name;
				}
				return null;
			}
		}
		#endregion

		#region Events
		protected virtual void OnSequenceStarted(SequenceStartedEventArgs e) {
			if(SequenceStarted != null) {
				SequenceStarted(null, e);
			}
		}

		protected virtual void OnSequenceEnded(SequenceEventArgs e) {
			if(SequenceEnded != null) {
				SequenceEnded(null, e);
			}
		}

		protected virtual void OnMessage(ExecutorMessageEventArgs e) {
			if(Message != null) {
				Message(null, e);
			}
		}

		protected virtual void OnError(ExecutorMessageEventArgs e) {
			if(Error != null) {
				Error(Sequence, e);
			}
		}
		#endregion

		#region Private
		private void _Play(TimeSpan startTime, TimeSpan endTime) {
			if(IsRunning) return;
			if(Sequence == null) return;

			// Only hook the input stream during execution.
			// Hook before starting the behaviors.
			_HookDataListener();

			// Bound the execution range.
			StartTime = _CoerceStartTime(startTime);
			EndTime = _CoerceEndTime(endTime);

			TimingSource = Sequence.GetTiming() ?? _GetDefaultTimingSource();

			_LoadMedia();

			// Start the crazy train.
			IsRunning = true;

			_StartMedia();

			TimingSource.Position = StartTime;
			TimingSource.Start();

			// Fire the first event manually because it takes a while for the timer
			// to elapse the first time.
			_CheckForNaturalEnd();

			// If there is no length, we may have been stopped as a cascading result
			// of that update.
			if(IsRunning) {
				_endCheckTimer.Start();
			}
		}

		protected virtual void _HookDataListener() {
			Sequence.InsertDataListener += _DataListener;
		}

		protected virtual void _UnhookDataListener() {
			Sequence.InsertDataListener -= _DataListener;
		}

		protected virtual TimeSpan _CoerceStartTime(TimeSpan startTime) {
			return startTime < Sequence.Length ? startTime : Sequence.Length;
		}

		protected virtual TimeSpan _CoerceEndTime(TimeSpan endTime) {
			return endTime < Sequence.Length ? endTime : Sequence.Length;
		}

		protected virtual ITiming _GetDefaultTimingSource() {
			return TimingService.Instance.GetDefaultSequenceTiming();
		}

		protected virtual void _LoadMedia() {
			foreach(IMediaModuleInstance media in Sequence.GetAllMedia()) {
				media.LoadMedia(StartTime);
			}
		}

		protected virtual void _StartMedia() {
			foreach(IMediaModuleInstance media in Sequence.GetAllMedia()) {
				media.Start();
			}
		}

		protected virtual void _PauseMedia() {
			foreach(IMediaModuleInstance media in Sequence.GetAllMedia()) {
				media.Pause();
			}
		}

		protected virtual void _ResumeMedia() {
			foreach(IMediaModuleInstance media in Sequence.GetAllMedia()) {
				media.Resume();
			}
		}

		protected virtual void _StopMedia() {
			foreach(IMediaModuleInstance media in Sequence.GetAllMedia()) {
				media.Stop();
			}
		}

		private void _Pause() {
			if(!IsRunning || IsPaused) return;
			
			if(_endCheckTimer.Enabled) {
				IsPaused = true;

				TimingSource.Pause();

				_PauseMedia();
					
				_endCheckTimer.Enabled = false;
			}
		}

		private void _Resume() {
			if(!IsPaused) return;

			if(!_endCheckTimer.Enabled && Sequence != null) {
				IsPaused = false;
	
				TimingSource.Resume();

				_ResumeMedia();

				_endCheckTimer.Enabled = true;
			}
		}

		private void _Stop() {
			if(!IsRunning) return;

			// Stop whatever is driving this crazy train.
			lock(_endCheckTimer) {
				_endCheckTimer.Enabled = false;
			}

			// Release the hook before the behaviors are shut down so that
			// they can affect the sequence.
			_UnhookDataListener();

			IsRunning = false;

			TimingSource.Stop();
	
			_StopMedia();
		}

		public ITiming TimingSource { get; private set; }

		protected virtual bool _DataListener(IEffectNode effectNode) {
			// We don't want any handlers beyond the executor to get live data.
			return true;
		}

		private void _EndCheckTimerElapsed(object sender, ElapsedEventArgs e) {
			lock(_endCheckTimer) {
				// To catch events that may trail after the timer's been disabled
				// due to it being a threaded timer and Stop being called between the
				// timer message being posted and acted upon.
				if(_endCheckTimer == null || !_endCheckTimer.Enabled) return;

				_endCheckTimer.Enabled = false;

				_CheckForNaturalEnd();

				if(IsRunning) {
					_endCheckTimer.Enabled = true;
				}
			}
		}

		private void _CheckForNaturalEnd() {
			if(_IsEndOfSequence()) {
				_syncContext.Post(x => _Stop(), null);
			}
		}

		private bool _IsEndOfSequence() {
			return _IsTimedSequence && TimingSource.Position >= EndTime;
		}

		protected bool _IsTimedSequence {
			get { return EndTime >= StartTime; }
		}

		#endregion

		#region Dispose
		~SequenceExecutor() {
			Dispose(false);
		}

		public void Dispose() {
			Dispose(true);
		}

		public virtual void Dispose(bool disposing) {
			if(_endCheckTimer != null) {
				lock(_endCheckTimer) {
					Stop();
					_endCheckTimer.Elapsed -= _EndCheckTimerElapsed;
					_endCheckTimer.Dispose();
					_endCheckTimer = null;
				}
			}
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
