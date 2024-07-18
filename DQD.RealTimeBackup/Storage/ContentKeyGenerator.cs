using System;

namespace DQD.RealTimeBackup.Storage
{
	public class ContentKeyGenerator : IContentKeyGenerator
	{
		OperatingParameters _parameters;

		public ContentKeyGenerator(OperatingParameters parameters)
		{
			_parameters = parameters;
		}

		public string GenerateContentKey()
		{
			char[] contentKeyChars = new char[_parameters.ContentKeyLength];

			Random rnd = new Random();

			for (int i=0; i < contentKeyChars.Length; i++)
				contentKeyChars[i] = _parameters.ContentKeyAlphabet[rnd.Next(_parameters.ContentKeyAlphabet.Length)];

			return new string(contentKeyChars);
		}
	}
}
