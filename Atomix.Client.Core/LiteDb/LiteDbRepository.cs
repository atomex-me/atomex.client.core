using System;
using System.Security;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.Ethereum;
using Atomix.Blockchain.Tezos;
using Atomix.Common;
using Atomix.Common.Bson;

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
            new TezosTransactionToBsonSerializer().Register();
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