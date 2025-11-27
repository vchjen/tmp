using System;

namespace MatchingEngineOptimized
{
    public enum Side : byte
    {
        Buy = 0,
        Sell = 1
    }

    public readonly struct Trade
    {
        public readonly decimal Price;
        public readonly int Quantity;
        public readonly int TakerOrderId;
        public readonly int MakerOrderId;

        public Trade(decimal price, int quantity, int takerId, int makerId)
        {
            Price = price;
            Quantity = quantity;
            TakerOrderId = takerId;
            MakerOrderId = makerId;
        }
    }

    public struct Order
    {
        public int Id;          // simple int id
        public Side Side;
        public decimal Price;
        public int Quantity;    // remaining
        public long Sequence;   // time priority

        public bool Active => Quantity > 0;
    }

    /// <summary>
    /// Fixed-size ring buffer queue (array-backed).
    /// </summary>
    public struct RingQueue<T>
    {
        private T[] _buffer;
        private int _head;
        private int _tail;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public bool IsFull => _count == _buffer.Length;
        public bool IsEmpty => _count == 0;

        public RingQueue(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new T[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        public bool TryEnqueue(T item)
        {
            if (_count == _buffer.Length) return false;
            _buffer[_tail] = item;
            _tail++;
            if (_tail == _buffer.Length) _tail = 0;
            _count++;
            return true;
        }

        public bool TryDequeue(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_head];
            _head++;
            if (_head == _buffer.Length) _head = 0;
            _count--;
            return true;
        }

        public bool TryPeek(out T item)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_head];
            return true;
        }
    }

    public struct PriceLevel
    {
        public decimal Price;
        public RingQueue<int> Queue; // indices into orders[]
        public bool InUse;

        public void Init(decimal price, int perLevelCapacity)
        {
            Price = price;
            Queue = new RingQueue<int>(perLevelCapacity);
            InUse = true;
        }

        public bool IsEmpty => !InUse || Queue.Count == 0;
    }

    /// <summary>
    /// Price book for one side (bids OR asks), using a sorted array of PriceLevel.
    /// </summary>
    public sealed class PriceBook
    {
        private readonly PriceLevel[] _levels;
        private int _count;
        private readonly bool _isBids;
        private readonly int _perLevelCapacity;

        public PriceBook(bool isBids, int maxLevels, int perLevelCapacity)
        {
            _isBids = isBids;
            _levels = new PriceLevel[maxLevels];
            _count = 0;
            _perLevelCapacity = perLevelCapacity;
        }

        public int Count => _count;

        public ref PriceLevel GetLevel(int index) => ref _levels[index];

        /// <summary>
        /// Find level index by price. Returns -1 if not found.
        /// Levels are kept sorted ascending by price.
        /// </summary>
        public int FindLevelIndex(decimal price)
        {
            int lo = 0;
            int hi = _count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                decimal midPrice = _levels[mid].Price;

                if (midPrice == price) return mid;
                if (midPrice < price) lo = mid + 1;
                else hi = mid - 1;
            }

            return -1;
        }

        /// <summary>
        /// Get or create a price level for given price. Keeps array sorted.
        /// </summary>
        public int GetOrAddLevel(decimal price)
        {
            // binary search for insertion point
            int lo = 0;
            int hi = _count - 1;
            int insertPos = 0;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                decimal midPrice = _levels[mid].Price;

                if (midPrice == price) return mid;

                if (midPrice < price)
                {
                    lo = mid + 1;
                    insertPos = lo;
                }
                else
                {
                    hi = mid - 1;
                    insertPos = mid;
                }
            }

            if (_count == _levels.Length)
                throw new InvalidOperationException("PriceBook is full (max levels).");

            // shift right to make space
            for (int i = _count; i > insertPos; i--)
            {
                _levels[i] = _levels[i - 1];
            }

            _levels[insertPos].Init(price, _perLevelCapacity);
            _count++;
            return insertPos;
        }

        /// <summary>
        /// Remove level at index (compacts array).
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _count) return;

            for (int i = index; i < _count - 1; i++)
            {
                _levels[i] = _levels[i + 1];
            }
            _count--;
        }

        /// <summary>
        /// Get best level index, ignoring eligibility vs taker price.
        /// For asks: lowest price (0), for bids: highest price (_count-1).
        /// Returns -1 if empty.
        /// </summary>
        public int GetBestIndex()
        {
            if (_count == 0) return -1;
            return _isBids ? _count - 1 : 0;
        }
    }

    /// <summary>
    /// Single-pair order book with fixed-size arrays and ring buffers.
    /// Single-threaded, in-memory POC.
    /// </summary>
    public sealed class SinglePairOrderBook
    {
        private readonly Order[] _orders;
        private int _orderCount;
        private long _nextSequence;

        private readonly PriceBook _bids;
        private readonly PriceBook _asks;

        public SinglePairOrderBook(
            int maxOrders,
            int maxPriceLevelsPerSide,
            int maxOrdersPerLevel)
        {
            _orders = new Order[maxOrders];
            _orderCount = 0;
            _nextSequence = 1;

            _bids = new PriceBook(isBids: true, maxLevels: maxPriceLevelsPerSide, perLevelCapacity: maxOrdersPerLevel);
            _asks = new PriceBook(isBids: false, maxLevels: maxPriceLevelsPerSide, perLevelCapacity: maxOrdersPerLevel);
        }

        public ref Order GetOrderByIndex(int idx) => ref _orders[idx];

        /// <summary>
        /// Place a limit order and match immediately.
        /// The caller supplies a trade buffer; returns number of trades filled into that buffer.
        /// </summary>
        public int PlaceOrder(
            Side side,
            decimal price,
            int quantity,
            Span<Trade> tradeBuffer)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (_orderCount >= _orders.Length)
                throw new InvalidOperationException("Order storage full.");

            int tradeCount = 0;

            // allocate taker slot in array
            int takerIndex = _orderCount++;
            ref var taker = ref _orders[takerIndex];
            taker.Id = takerIndex + 1; // simple ID = index+1
            taker.Side = side;
            taker.Price = price;
            taker.Quantity = quantity;
            taker.Sequence = _nextSequence++;

            if (side == Side.Buy)
            {
                MatchAgainstAsks(ref taker, takerIndex, tradeBuffer, ref tradeCount);
            }
            else
            {
                MatchAgainstBids(ref taker, takerIndex, tradeBuffer, ref tradeCount);
            }

            if (taker.Quantity > 0)
            {
                AddToBook(side, price, takerIndex);
            }

            return tradeCount;
        }

        private void MatchAgainstAsks(
            ref Order taker,
            int takerIndex,
            Span<Trade> tradeBuffer,
            ref int tradeCount)
        {
            while (taker.Quantity > 0 && _asks.Count > 0)
            {
                int bestIdx = _asks.GetBestIndex(); // lowest ask
                if (bestIdx < 0) break;

                ref var level = ref _asks.GetLevel(bestIdx);
                decimal bestPrice = level.Price;

                if (bestPrice > taker.Price)
                    break; // no more matching levels

                // walk FIFO in this price level
                while (taker.Quantity > 0 && !level.Queue.IsEmpty)
                {
                    if (!level.Queue.TryPeek(out int makerIdx))
                        break;

                    ref var maker = ref _orders[makerIdx];

                    if (!maker.Active)
                    {
                        level.Queue.TryDequeue(out _);
                        continue;
                    }

                    int tradedQty = taker.Quantity < maker.Quantity
                        ? taker.Quantity
                        : maker.Quantity;

                    maker.Quantity -= tradedQty;
                    taker.Quantity -= tradedQty;

                    if (tradeCount < tradeBuffer.Length)
                    {
                        tradeBuffer[tradeCount] = new Trade(
                            maker.Price,
                            tradedQty,
                            taker.Id,
                            maker.Id);
                    }
                    tradeCount++;

                    if (!maker.Active)
                    {
                        level.Queue.TryDequeue(out _);
                    }
                }

                if (level.Queue.Count == 0)
                {
                    _asks.RemoveAt(bestIdx);
                }
            }
        }

        private void MatchAgainstBids(
            ref Order taker,
            int takerIndex,
            Span<Trade> tradeBuffer,
            ref int tradeCount)
        {
            while (taker.Quantity > 0 && _bids.Count > 0)
            {
                int bestIdx = _bids.GetBestIndex(); // highest bid
                if (bestIdx < 0) break;

                ref var level = ref _bids.GetLevel(bestIdx);
                decimal bestPrice = level.Price;

                if (bestPrice < taker.Price)
                    break; // no more matching levels

                while (taker.Quantity > 0 && !level.Queue.IsEmpty)
                {
                    if (!level.Queue.TryPeek(out int makerIdx))
                        break;

                    ref var maker = ref _orders[makerIdx];

                    if (!maker.Active)
                    {
                        level.Queue.TryDequeue(out _);
                        continue;
                    }

                    int tradedQty = taker.Quantity < maker.Quantity
                        ? taker.Quantity
                        : maker.Quantity;

                    maker.Quantity -= tradedQty;
                    taker.Quantity -= tradedQty;

                    if (tradeCount < tradeBuffer.Length)
                    {
                        tradeBuffer[tradeCount] = new Trade(
                            maker.Price,
                            tradedQty,
                            taker.Id,
                            maker.Id);
                    }
                    tradeCount++;

                    if (!maker.Active)
                    {
                        level.Queue.TryDequeue(out _);
                    }
                }

                if (level.Queue.Count == 0)
                {
                    _bids.RemoveAt(bestIdx);
                }
            }
        }

        private void AddToBook(Side side, decimal price, int orderIndex)
        {
            var book = side == Side.Buy ? _bids : _asks;
            int levelIndex = book.GetOrAddLevel(price);
            ref var level = ref book.GetLevel(levelIndex);

            if (!level.Queue.TryEnqueue(orderIndex))
            {
                throw new InvalidOperationException("Price level ring buffer full.");
            }
        }

        public (decimal? bestBid, decimal? bestAsk) GetTopOfBook()
        {
            decimal? bid = null;
            decimal? ask = null;

            if (_bids.Count > 0)
            {
                int idx = _bids.GetBestIndex();
                if (idx >= 0)
                    bid = _bids.GetLevel(idx).Price;
            }

            if (_asks.Count > 0)
            {
                int idx = _asks.GetBestIndex();
                if (idx >= 0)
                    ask = _asks.GetLevel(idx).Price;
            }

            return (bid, ask);
        }
    }

    internal static class Program
    {
        private static void Main()
        {
            // Example usage
            var book = new SinglePairOrderBook(
                maxOrders: 100_000,
                maxPriceLevelsPerSide: 1024,
                maxOrdersPerLevel: 512);

            Span<Trade> tradeBuffer = stackalloc Trade[16];

            // Resting asks
            book.PlaceOrder(Side.Sell, 101m, 10, tradeBuffer);
            book.PlaceOrder(Side.Sell, 102m, 20, tradeBuffer);
            book.PlaceOrder(Side.Sell, 101m, 5, tradeBuffer); // same price, later in FIFO

            // Aggressive buy
            int tradeCount = book.PlaceOrder(Side.Buy, 102m, 18, tradeBuffer);

            Console.WriteLine("Trades:");
            for (int i = 0; i < tradeCount && i < tradeBuffer.Length; i++)
            {
                var t = tradeBuffer[i];
                Console.WriteLine($"price={t.Price}, qty={t.Quantity}, taker={t.TakerOrderId}, maker={t.MakerOrderId}");
            }

            var (bestBid, bestAsk) = book.GetTopOfBook();
            Console.WriteLine($"Top of book: bid={bestBid}, ask={bestAsk}");
        }
    }
}
