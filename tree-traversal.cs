using System;

namespace TreesDemo
{
    // ===========================
    // B-TREE (degree t, int keys)
    // ===========================

    public class BTreeNode
    {
        public int[] Keys;
        public BTreeNode[] Children;
        public int KeyCount;
        public bool Leaf;

        private readonly int _t; // minimum degree

        public BTreeNode(int t, bool leaf)
        {
            _t = t;
            Leaf = leaf;
            Keys = new int[2 * t - 1];
            Children = new BTreeNode[2 * t];
            KeyCount = 0;
        }

        // In-order-like traversal: prints keys sorted
        public void Traverse()
        {
            int i;
            for (i = 0; i < KeyCount; i++)
            {
                if (!Leaf)
                    Children[i].Traverse();
                Console.Write(Keys[i] + " ");
            }

            if (!Leaf)
                Children[i].Traverse();
        }

        // Insert a key into a non-full node
        public void InsertNonFull(int k)
        {
            int i = KeyCount - 1;

            if (Leaf)
            {
                // Shift keys to make space
                while (i >= 0 && Keys[i] > k)
                {
                    Keys[i + 1] = Keys[i];
                    i--;
                }

                Keys[i + 1] = k;
                KeyCount++;
            }
            else
            {
                // Find child to descend into
                while (i >= 0 && Keys[i] > k)
                    i--;

                i++;

                if (Children[i].KeyCount == 2 * _t - 1)
                {
                    SplitChild(i, Children[i]);

                    if (Keys[i] < k)
                        i++;
                }

                Children[i].InsertNonFull(k);
            }
        }

        // Split full child y at index i into two nodes y and z
        public void SplitChild(int i, BTreeNode y)
        {
            var z = new BTreeNode(_t, y.Leaf)
            {
                KeyCount = _t - 1
            };

            // Copy top (t-1) keys to z
            for (int j = 0; j < _t - 1; j++)
                z.Keys[j] = y.Keys[j + _t];

            // Copy children if not leaf
            if (!y.Leaf)
            {
                for (int j = 0; j < _t; j++)
                    z.Children[j] = y.Children[j + _t];
            }

            y.KeyCount = _t - 1;

            // Shift children in this node to make room for z
            for (int j = KeyCount; j >= i + 1; j--)
                Children[j + 1] = Children[j];

            Children[i + 1] = z;

            // Shift keys in this node
            for (int j = KeyCount - 1; j >= i; j--)
                Keys[j + 1] = Keys[j];

            Keys[i] = y.Keys[_t - 1];
            KeyCount++;
        }
    }

    public class BTree
    {
        private readonly int _t;
        private BTreeNode _root;

        public BTree(int t)
        {
            if (t < 2) throw new ArgumentOutOfRangeException(nameof(t), "Degree must be >= 2.");
            _t = t;
            _root = null!;
        }

        public void Insert(int k)
        {
            if (_root == null)
            {
                _root = new BTreeNode(_t, leaf: true);
                _root.Keys[0] = k;
                _root.KeyCount = 1;
                return;
            }

            if (_root.KeyCount == 2 * _t - 1)
            {
                var s = new BTreeNode(_t, leaf: false);
                s.Children[0] = _root;
                s.SplitChild(0, _root);

                int i = 0;
                if (s.Keys[0] < k)
                    i++;

                s.Children[i].InsertNonFull(k);
                _root = s;
            }
            else
            {
                _root.InsertNonFull(k);
            }
        }

        public void Traverse()
        {
            if (_root != null)
                _root.Traverse();
        }
    }

    // ===========================
    // RED-BLACK TREE (int keys)
    // ===========================

    public enum NodeColor : byte
    {
        Red,
        Black
    }

    public class RBNode
    {
        public int Key;
        public NodeColor Color;
        public RBNode Left;
        public RBNode Right;
        public RBNode Parent;

        public RBNode(int key)
        {
            Key = key;
            Color = NodeColor.Red;
        }
    }

    public class RedBlackTree
    {
        private RBNode _root;

        public void Insert(int key)
        {
            var newNode = new RBNode(key);
            BSTInsert(newNode);
            FixInsert(newNode);
        }

        private void BSTInsert(RBNode z)
        {
            RBNode y = null!;
            var x = _root;

            while (x != null)
            {
                y = x;
                if (z.Key < x.Key)
                    x = x.Left;
                else
                    x = x.Right;
            }

            z.Parent = y;

            if (y == null)
                _root = z;
            else if (z.Key < y.Key)
                y.Left = z;
            else
                y.Right = z;
        }

        private void FixInsert(RBNode z)
        {
            while (z != _root && z.Parent.Color == NodeColor.Red)
            {
                if (z.Parent == z.Parent.Parent.Left)
                {
                    var y = z.Parent.Parent.Right; // uncle
                    if (y != null && y.Color == NodeColor.Red)
                    {
                        // Case 1: uncle is red
                        z.Parent.Color = NodeColor.Black;
                        y.Color = NodeColor.Black;
                        z.Parent.Parent.Color = NodeColor.Red;
                        z = z.Parent.Parent;
                    }
                    else
                    {
                        if (z == z.Parent.Right)
                        {
                            // Case 2: z is right child
                            z = z.Parent;
                            RotateLeft(z);
                        }
                        // Case 3:
                        z.Parent.Color = NodeColor.Black;
                        z.Parent.Parent.Color = NodeColor.Red;
                        RotateRight(z.Parent.Parent);
                    }
                }
                else
                {
                    var y = z.Parent.Parent.Left; // uncle
                    if (y != null && y.Color == NodeColor.Red)
                    {
                        // Mirror Case 1
                        z.Parent.Color = NodeColor.Black;
                        y.Color = NodeColor.Black;
                        z.Parent.Parent.Color = NodeColor.Red;
                        z = z.Parent.Parent;
                    }
                    else
                    {
                        if (z == z.Parent.Left)
                        {
                            // Mirror Case 2
                            z = z.Parent;
                            RotateRight(z);
                        }
                        // Mirror Case 3
                        z.Parent.Color = NodeColor.Black;
                        z.Parent.Parent.Color = NodeColor.Red;
                        RotateLeft(z.Parent.Parent);
                    }
                }
            }

            _root.Color = NodeColor.Black;
        }

        private void RotateLeft(RBNode x)
        {
            var y = x.Right;
            x.Right = y.Left;
            if (y.Left != null)
                y.Left.Parent = x;

            y.Parent = x.Parent;
            if (x.Parent == null)
                _root = y;
            else if (x == x.Parent.Left)
                x.Parent.Left = y;
            else
                x.Parent.Right = y;

            y.Left = x;
            x.Parent = y;
        }

        private void RotateRight(RBNode x)
        {
            var y = x.Left;
            x.Left = y.Right;
            if (y.Right != null)
                y.Right.Parent = x;

            y.Parent = x.Parent;
            if (x.Parent == null)
                _root = y;
            else if (x == x.Parent.Right)
                x.Parent.Right = y;
            else
                x.Parent.Left = y;

            y.Right = x;
            x.Parent = y;
        }

        // In-order traversal (sorted order)
        public void InOrderTraversal()
        {
            InOrderTraversal(_root);
        }

        private void InOrderTraversal(RBNode node)
        {
            if (node == null) return;
            InOrderTraversal(node.Left);
            Console.Write(node.Key + " ");
            InOrderTraversal(node.Right);
        }
    }

    // ===========================
    // DEMO
    // ===========================

    internal static class Program
    {
        private static void Main()
        {
            int[] keys = { 10, 20, 5, 6, 12, 30, 7, 17 };

            Console.WriteLine("B-Tree traversal (degree = 3):");
            var btree = new BTree(t: 3);
            foreach (var k in keys)
                btree.Insert(k);
            btree.Traverse();
            Console.WriteLine();

            Console.WriteLine("\nRed-Black Tree in-order traversal:");
            var rbt = new RedBlackTree();
            foreach (var k in keys)
                rbt.Insert(k);
            rbt.InOrderTraversal();
            Console.WriteLine();
        }
    }
}
