using Atomix.Common;
using Atomix.Common.Bson;
using System;
using System.Security;

namespace Atomix.LiteDb
{
    public abstract class LiteDbRepository
    {
        private readonly string _pathToDb;
        private readonly string _sessionPassword;

        protected string ConnectionString => $"FileName={_pathToDb};Password={_sessionPassword}";

        static LiteDbRepository()
        {
            new CurrencyToBsonSerializer().Register();
            new SymbolToBsonSerializer().Register();
            new BitcoinBasedTransactionToBsonSerializer().Register();
            new BitcoinBasedTxOutputToBsonSerializer().Register();
            new EthereumTransactionToBsonSerializer().Register();
            new SwapToBsonSerializer().Register();
        }

        protected LiteDbRepository(string pathToDb, SecureString password)
        {
            _pathToDb = pathToDb ?? throw new ArgumentNullException(nameof(pathToDb));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            _sessionPassword = SessionPasswordHelper.GetSessionPassword(password);
        }
    }
}