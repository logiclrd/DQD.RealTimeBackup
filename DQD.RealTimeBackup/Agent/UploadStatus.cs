using System;
using System.ComponentModel;

using DQD.RealTimeBackup.Bridge.Serialization;
using DQD.RealTimeBackup.Storage;

namespace DQD.RealTimeBackup.Agent
{
	public class UploadStatus
	{
		[FieldOrder(0)]
		public string Path;
		[FieldOrder(1)]
		public long FileSize;
		[FieldOrder(2)]
		public UploadProgress? Progress;

		public Action RecheckFunctor;

		bool _isCompleted;
		bool _recheckAfterUploadCompletes;

		public void RecheckAfterUploadCompletes()
		{
			lock (this)
			{
				if (_isCompleted)
				{
					RecheckFunctor();
					RecheckFunctor = () => {};
				}
				else
					_recheckAfterUploadCompletes = true;
			}
		}

		public void MarkCompleted()
		{
			lock (this)
			{
				_isCompleted = true;

				if (_recheckAfterUploadCompletes)
					RecheckAfterUploadCompletes();
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		public UploadStatus()
		{
			RecheckFunctor = () => {};
			Path = "";
		}

		public UploadStatus(string path, Action recheckFunctor)
		{
			Path = path;
			RecheckFunctor = recheckFunctor;
		}

		public UploadStatus(string path, long fileSize, Action recheckFunctor)
		{
			Path = path;
			FileSize = fileSize;
			RecheckFunctor = recheckFunctor;
		}
	}
}
