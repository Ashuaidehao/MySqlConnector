using System;
using System.Text;
using MySqlConnector.Protocol.Serialization;
using MySqlConnector.Utilities;

namespace MySqlConnector.Protocol.Payloads
{
	internal sealed class OkPayload
	{
		public int AffectedRowCount { get; }
		public ulong LastInsertId { get; }
		public ServerStatus ServerStatus { get; }
		public int WarningCount { get; }
		public string NewSchema { get; }

		public const byte Signature = 0x00;

		/* See
		 * http://web.archive.org/web/20160604101747/http://dev.mysql.com/doc/internals/en/packet-OK_Packet.html
		 * https://mariadb.com/kb/en/the-mariadb-library/resultset/
		 * https://github.com/MariaDB/mariadb-connector-j/blob/5fa814ac6e1b4c9cb6d141bd221cbd5fc45c8a78/src/main/java/org/mariadb/jdbc/internal/com/read/resultset/SelectResultSet.java#L443-L444
		 */
		public static bool IsOk(PayloadData payload, bool deprecateEof) =>
			payload.ArraySegment.Array != null && payload.ArraySegment.Count > 0 &&
				((payload.ArraySegment.Count > 6 && payload.ArraySegment.Array[payload.ArraySegment.Offset] == Signature) ||
				(deprecateEof && payload.ArraySegment.Count < 0xFF_FFFF && payload.ArraySegment.Array[payload.ArraySegment.Offset] == EofPayload.Signature));

		public static OkPayload Create(PayloadData payload) => Create(payload, false);

		public static OkPayload Create(PayloadData payload, bool deprecateEof)
		{
			var reader = new ByteArrayReader(payload.ArraySegment);
			var signature = reader.ReadByte();
			if (signature != Signature && (!deprecateEof || signature != EofPayload.Signature))
				throw new FormatException("Expected to read 0x00 or 0xFE but got 0x{0:X2}".FormatInvariant(signature));
			var affectedRowCount = checked((int) reader.ReadLengthEncodedInteger());
			var lastInsertId = reader.ReadLengthEncodedInteger();
			var serverStatus = (ServerStatus) reader.ReadUInt16();
			var warningCount = (int) reader.ReadUInt16();
			string newSchema = null;

			if ((serverStatus & ServerStatus.SessionStateChanged) == ServerStatus.SessionStateChanged)
			{
				reader.ReadLengthEncodedByteString(); // human-readable info

				// implies ProtocolCapabilities.SessionTrack
				var sessionStateChangeDataLength = checked((int) reader.ReadLengthEncodedInteger());
				var endOffset = reader.Offset + sessionStateChangeDataLength;
				while (reader.Offset < endOffset)
				{
					var kind = (SessionTrackKind) reader.ReadByte();
					var dataLength = (int) reader.ReadLengthEncodedInteger();
					switch (kind)
					{
					case SessionTrackKind.Schema:
						newSchema = Encoding.UTF8.GetString(reader.ReadLengthEncodedByteString());
						break;

					default:
						reader.Offset += dataLength;
						break;
					}
				}
			}
			else
			{
				// either "string<EOF> info" or "string<lenenc> info" (followed by no session change info)
				// ignore human-readable string in both cases
			}

			return new OkPayload(affectedRowCount, lastInsertId, serverStatus, warningCount, newSchema);
		}

		private OkPayload(int affectedRowCount, ulong lastInsertId, ServerStatus serverStatus, int warningCount, string newSchema)
		{
			AffectedRowCount = affectedRowCount;
			LastInsertId = lastInsertId;
			ServerStatus = serverStatus;
			WarningCount = warningCount;
			NewSchema = newSchema;
		}
	}
}
