using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.SqlServer.Server;

namespace InfiniteLoopInSystemDataRepro
{
	class Program
	{
		static string ConnectionString = @"Data Source=(localdb)\SystemDataRepro;Database=SystemDataRepro;Integrated Security=true;";

		// Database setup, 
		// from powershell: sqllocaldb create SystemDataRepro 13.0
		// then connect and run:
		/*
		create database SystemDataRepro

		use SystemDataRepro;

		create table TestTable(someData varbinary(8000));

		create type TestTableType as table(someData varbinary(8000));

		go
		create procedure TestSproc @rows TestTableType readonly
		as
		begin
			set nocount on;
			insert into TestTable select * from @rows
		end
		*/

		// Reproduce infinite loop bug inside TdsParserStateObject.WriteByteArray when _outputPacketNumber rolls around from 255 to 0 and a specific byte sequence appears in _outBuff in bytes 8-11
		static void Main(string[] args)
		{
			Console.WriteLine("Executing commit");

			CommitData().Wait();

			Console.WriteLine("Done");
			Console.ReadLine();
		}

		static async Task CommitData()
		{
			using (var connection = new SqlConnection(ConnectionString))
			using (var cmd = new SqlCommand("TestSproc") {
				CommandType = System.Data.CommandType.StoredProcedure,
				Connection = connection,
			})
			{
				await cmd.Connection.OpenAsync();
				cmd.Parameters.Add(new SqlParameter("@rows", SqlDbType.Structured) {
					TypeName = "TestTableType",
					Value = new RowEnumerator(),
				});

				await cmd.ExecuteNonQueryAsync();
			}
		}


		/// <summary>
		/// Class that passes the rows of table type to sproc parameter
		/// </summary>
		class RowEnumerator : IEnumerable<SqlDataRecord>, IEnumerator<SqlDataRecord>
		{
			int _count = 0;
			SqlDataRecord _record;

			public RowEnumerator()
			{
				_record = new SqlDataRecord(new SqlMetaData("someData", SqlDbType.VarBinary, 8000));

				// 56 31 0 0 is result of calling BitConverter.GetBytes((int)7992)
				// The rest of the bytes are just padding to get 56, 31, 0, 0 to be in bytes 8-11 of TdsParserStatObject._outBuff after the 256th packet
				_record.SetBytes(
					0,
					0,
					new byte[] { 1, 2, 56, 31, 0, 0, 7, 8, 9, 10, 11, 12, 13, 14 },
					0,
					14);

				// change any of the 56 31 0 0 bytes and this program completes as expected in a couple seconds
			}

			public SqlDataRecord Current
			{
				get
				{
					return _record;
				}
			}

			object IEnumerator.Current
			{
				get
				{
					return Current;
				}
			}

			public IEnumerator<SqlDataRecord> GetEnumerator()
			{
				return this;
			}

			public bool MoveNext()
			{
				_count++;
				if (_count % 100 == 0)
					Console.Write(".");
				return _count < 1000000;

			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			public void Dispose() { }
			public void Reset() { }
		}
	}
}
