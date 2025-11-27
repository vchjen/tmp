using System;

namespace SortingDemo
{
    public static class QuickSorter
    {
        public static void QuickSort(int[] arr)
        {
            if (arr == null || arr.Length <= 1)
                return;

            QuickSort(arr, 0, arr.Length - 1);
        }

        private static void QuickSort(int[] arr, int left, int right)
        {
            int i = left;
            int j = right;
            int pivot = arr[left + (right - left) / 2];

            while (i <= j)
            {
                while (arr[i] < pivot) i++;
                while (arr[j] > pivot) j--;

                if (i <= j)
                {
                    if (i != j)
                    {
                        int tmp = arr[i];
                        arr[i] = arr[j];
                        arr[j] = tmp;
                    }
                    i++;
                    j--;
                }
            }

            if (left < j)
                QuickSort(arr, left, j);
            if (i < right)
                QuickSort(arr, i, right);
        }
    }

    public static class MergeSorter
    {
        public static void MergeSort(int[] arr)
        {
            if (arr == null || arr.Length <= 1)
                return;

            int[] aux = new int[arr.Length];
            MergeSort(arr, aux, 0, arr.Length - 1);
        }

        private static void MergeSort(int[] arr, int[] aux, int left, int right)
        {
            if (left >= right)
                return;

            int mid = left + (right - left) / 2;

            MergeSort(arr, aux, left, mid);
            MergeSort(arr, aux, mid + 1, right);

            Merge(arr, aux, left, mid, right);
        }

        private static void Merge(int[] arr, int[] aux, int left, int mid, int right)
        {
            int i = left;
            int j = mid + 1;
            int k = left;

            while (i <= mid && j <= right)
            {
                if (arr[i] <= arr[j])
                    aux[k++] = arr[i++];
                else
                    aux[k++] = arr[j++];
            }

            while (i <= mid)
                aux[k++] = arr[i++];

            while (j <= right)
                aux[k++] = arr[j++];

            for (int idx = left; idx <= right; idx++)
                arr[idx] = aux[idx];
        }
    }

    public static class BubbleSorter
    {
        public static void BubbleSort(int[] arr)
        {
            if (arr == null || arr.Length <= 1)
                return;

            int n = arr.Length;
            bool swapped;

            for (int i = 0; i < n - 1; i++)
            {
                swapped = false;

                for (int j = 0; j < n - i - 1; j++)
                {
                    if (arr[j] > arr[j + 1])
                    {
                        int tmp = arr[j];
                        arr[j] = arr[j + 1];
                        arr[j + 1] = tmp;
                        swapped = true;
                    }
                }

                if (!swapped)
                    break;
            }
        }
    }

    class Program
    {
        static void Main()
        {
            int[] original = { 9, 4, 1,
