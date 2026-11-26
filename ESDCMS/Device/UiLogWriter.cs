using System;
using System.IO;
using System.Text;

namespace ESDCMS.Device;

public class UiLogWriter : TextWriter
{
	private readonly Action<string> _onLine;

	private readonly TextWriter _inner;

	private readonly StringBuilder _buffer = new StringBuilder();

	private readonly object _lock = new object();

	public override Encoding Encoding => Encoding.UTF8;

	public UiLogWriter(Action<string> onLine, TextWriter inner)
	{
		_onLine = onLine;
		_inner = inner;
	}

	public override void Write(char value)
	{
		_inner?.Write(value);
		lock (_lock)
		{
			switch (value)
			{
			case '\n':
				FlushLine();
				break;
			default:
				_buffer.Append(value);
				break;
			case '\r':
				break;
			}
		}
	}

	public override void Write(string value)
	{
		if (value != null)
		{
			foreach (char c in value)
			{
				Write(c);
			}
		}
	}

	private void FlushLine()
	{
		if (_buffer.Length != 0)
		{
			string line = _buffer.ToString();
			_buffer.Clear();
			_onLine?.Invoke(line);
		}
	}

	public override void Flush()
	{
		lock (_lock)
		{
			FlushLine();
		}
		_inner?.Flush();
	}
}
