using System;
using System.IO;

namespace W.Dm;

public class DmBLobStream : Stream
{
	private readonly DmBlob m_BLob;
	private bool m_Closed;
	private long m_CurPos;

	public override bool CanRead => !m_Closed;
	public override bool CanSeek => !m_Closed;
	public override bool CanWrite => !m_Closed && m_BLob.updateable;
	public override bool CanTimeout => false;

	public override long Length
	{
		get
		{
			EnsureOpen();
			return m_BLob.Length();
		}
	}

	public override long Position
	{
		get
		{
			EnsureOpen();
			return m_CurPos;
		}
		set
		{
			EnsureOpen();
			if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
			m_CurPos = value;
		}
	}

	public DmBLobStream(DmBlob bLob)
	{
		m_BLob = bLob ?? throw new ArgumentNullException(nameof(bLob));
	}

	public bool StreamCheck() => m_Closed;

	protected override void Dispose(bool disposing)
	{
		m_Closed = true;
		base.Dispose(disposing);
	}

	public override void Flush()
	{
		EnsureOpen();
		m_BLob.ValidateReadOwner();
		// LOB writes are sent immediately; the stream owns no buffered data.
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		EnsureOpen();
		ValidateBuffer(buffer, offset, count);
		m_BLob.ValidateReadOwner();
		if (count == 0 || m_CurPos >= m_BLob.Length()) return 0;
		byte[] bytes = m_BLob.GetBytes(m_CurPos, count);
		Array.Copy(bytes, 0, buffer, offset, bytes.Length);
		m_CurPos += bytes.Length;
		return bytes.Length;
	}

	public override int ReadByte()
	{
		byte[] buffer = new byte[1];
		return Read(buffer, 0, 1) == 0 ? -1 : buffer[0];
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		EnsureOpen();
		long start;
		switch (origin)
		{
		case SeekOrigin.Begin:
			start = 0;
			break;
		case SeekOrigin.Current:
			start = m_CurPos;
			break;
		case SeekOrigin.End:
			start = m_BLob.Length();
			break;
		default:
			throw new ArgumentException("Invalid seek origin.", nameof(origin));
		}
		long position;
		try { position = checked(start + offset); }
		catch (OverflowException ex) { throw new IOException("Seek position exceeds the supported range.", ex); }
		if (position < 0) throw new IOException("Cannot seek before the beginning of the stream.");
		m_CurPos = position;
		return position;
	}

	public override void SetLength(long value)
	{
		EnsureOpen();
		if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
		EnsureWritable();
		if (value > m_BLob.Length()) throw new NotSupportedException("LOB streams cannot extend their length without writing data.");
		m_BLob.Truncate(value);
		if (m_CurPos > value) m_CurPos = value;
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		EnsureOpen();
		ValidateBuffer(buffer, offset, count);
		EnsureWritable();
		long end;
		try { end = checked(m_CurPos + count); }
		catch (OverflowException ex) { throw new IOException("Write position exceeds the supported range.", ex); }
		m_BLob.ValidateReadOwner();
		if (count == 0) return;
		if (m_CurPos > m_BLob.Length()) throw new NotSupportedException("LOB streams cannot write a gap beyond the current length.");
		if ((m_BLob.local || m_BLob.fetchAll || m_BLob.storageType == AbstractLob.STORAGE_IN_ROW) && end > int.MaxValue)
			throw new NotSupportedException("The materialized LOB cannot represent the requested length.");
		int written = m_BLob.SetBytes(m_CurPos, ref buffer, offset, count);
		m_CurPos += written;
		if (written != count) throw new IOException("The LOB did not accept the complete write.");
	}

	private void EnsureOpen()
	{
		if (m_Closed) throw new ObjectDisposedException(nameof(DmBLobStream));
	}

	private void EnsureWritable()
	{
		if (!m_BLob.updateable) throw new NotSupportedException("The LOB is read-only.");
	}

	private static void ValidateBuffer(byte[] buffer, int offset, int count)
	{
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
		if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
		if (offset > buffer.Length || count > buffer.Length - offset)
			throw new ArgumentException("Offset and count exceed the buffer bounds.");
	}
}
