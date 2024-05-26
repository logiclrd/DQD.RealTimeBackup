using DeltaQ.RTB.Storage;

namespace DeltaQ.RTB.Agent
{
	public class UploadStatus
	{
		public string Path;
		public long FileSize;
		public UploadProgress? Progress;

		public UploadStatus(string path)
		{
			Path = path;
		}

		public UploadStatus(string path, long fileSize)
		{
			Path = path;
			FileSize = fileSize;
		}

		public string Format(int maxChars)
		{
			if (Progress == null)
				return "";

			string speed;

			if (Progress.BytesPerSecond > 80000000)
				speed = (Progress.BytesPerSecond / 1048576.0).ToString("#,##0.0") + " mb/s";
			else if (Progress.BytesPerSecond > 102400)
				speed = (Progress.BytesPerSecond / 1024.0).ToString("#,##0.0") + " kb/s";
			else
				speed = Progress.BytesPerSecond.ToString("#,##0") + "    b/s";

			speed = speed.PadLeft(12);

			double byteScale;
			string byteFormat;
			string byteUnit;

			if (FileSize < 1024)
			{
				byteScale = 1.0;
				byteFormat = "  #,##0";
				byteUnit = "  b";
			}
			else if (FileSize < 1024 * 1024)
			{
				byteScale = 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " kb";
			}
			else if (FileSize < 800 * 1024 * 1024)
			{
				byteScale = 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " mb";
			}
			else if (FileSize < 800 * 1024 * 1024 * 1024L)
			{
				byteScale = 1024 * 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " gb";
			}
			else
			{
				byteScale = 1024 * 1024 * 1024 * 1024.0;
				byteFormat = "#,##0.0";
				byteUnit = " tb";
			}

			string bytesTransferred = (Progress.BytesTransferred / byteScale).ToString(byteFormat).PadLeft(byteFormat.Length);
			string fileSize = (FileSize / byteScale).ToString(byteFormat).PadLeft(byteFormat.Length);

			string bytes = bytesTransferred + "/" + fileSize + byteUnit;

			string formattedLine = speed + " [" + bytes + "] " + Path;

			if (formattedLine.Length > maxChars)
				formattedLine = formattedLine.Substring(0, maxChars);

			return formattedLine;
		}
	}
}
