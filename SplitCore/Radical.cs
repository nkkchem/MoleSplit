﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MoleSplit
{
    /// <summary>
    /// 子图识别器
    /// </summary>
    class Radical : ARecognizer, IAddAttribute
    {
        private class Subgraph
        {
            /// <summary>
            /// 基团名称
            /// </summary>
            public string Name { get; private set; }
            /// <summary>
            /// 待判断的基团属性
            /// </summary>
            public string[] Tag { get; private set; }
            /// <summary>
            /// 基团邻接矩阵的条件：
            /// (1)第一个元素必须是与母体相连的原子（一般为C）；
            /// (2)原子顺序必须逐层排列
            /// </summary>
            public int[][] AdjMat { get; private set; }
            /// <summary>
            /// 原子列表（对应邻接矩阵）
            /// </summary>
            public Regex[] AtomCodeList { get; private set; }
            /// <summary>
            /// 重命名原子 或 辅助定位原子的坐标
            /// </summary>
            public int[] SpecialAtom { get; set; }
            public Subgraph(string text)
            {
                var info = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var tags = info[0].Split(new char[] { '?', ':' }, StringSplitOptions.RemoveEmptyEntries);

                this.AdjMat = new int[info.Length - 2][];
                this.AtomCodeList = new Regex[info.Length - 1];
                this.Name = tags[0];
                this.Tag = new string[0];
                if (tags.Length > 1)
                {
                    this.Tag = new string[tags.Length - 1];
                    for (int i = 0; i < this.Tag.Length; i++)
                    {
                        this.Tag[i] = tags[i + 1];
                    }
                }
                for (int i = 1; i < info.Length; i++)
                {
                    string[] element = info[i].Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string regexStr = '^' + element[0];
                    regexStr = regexStr.Replace("_", "_(_.+?)*_");
                    regexStr = regexStr.Replace("$", @"_$");
                    if (regexStr.Length - 1 == element[0].Length) { regexStr += '_'; }
                    this.AtomCodeList[i - 1] = new Regex(regexStr, RegexOptions.Compiled);
                    int[] temp = new int[i - 1];
                    for (int k = 0; k < temp.Length; k++)
                    {
                        temp[k] = int.Parse(element[k + 1]);
                    }
                    if (temp.Length != 0) { this.AdjMat[i - 2] = temp; }
                }
            }
        }
        // ---------------------------------------------------------------------------------
        private List<Subgraph> _radicalToMatch; // 用于匹配的子图
        private List<Subgraph> _radicalToRename; // 用于重命名的子图
        // ---------------------------------------------------------------------------------
        private Subgraph _radical; // 指向当前正在解析的子图
        private bool _isBreak; // 中断递归
        private int[] _matched; // 记录已匹配的原子索引
        private int _nAtom; // 原子个数
        private int _Sign; // 给原子打上的状态码
        private bool[] _lock; // 在原子的匹配过程中锁住正在使用的原子
        // ---------------------------------------------------------------------------------
        public override void Load(string text)
        {
            this._radicalToMatch = new List<Subgraph>();
            this._radicalToRename = new List<Subgraph>();

            var item = text.Split(new string[] { "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var r = new Regex(@"=\((.+?)\)", RegexOptions.Compiled);
            for (int i = 0; i < item.Length; i++)
            {
                string[] temp = r.Match(item[i]).Groups[1].Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries); // 提取附加坐标
                int[] tempInt = new int[temp.Length];
                for (int j = 0; j < temp.Length; j++)
                {
                    tempInt[j] = int.Parse(temp[j]);
                }
                string tempInfo = r.Replace(item[i], "");
                var tempRadical = new Subgraph(tempInfo);
                tempRadical.SpecialAtom = tempInt;

                switch (item[i][0])
                {
                    case '_': this._radicalToRename.Add(tempRadical);
                        break;
                    case '*': this._radicalToMatch.Add(tempRadical);
                        break;
                    default:
                        if (tempInt.Length > 0)
                        {
                            this._radicalToRename.Add(tempRadical);
                        }
                        else
                        {
                            this._radicalToMatch.Add(tempRadical);
                        }
                        break;
                }
            }
        }
        public override void Parse()
        {
            base.DefinedFragment = new Dictionary<string, int>();
            this._nAtom = base.Molecule.AtomList.Length; // 取出原子个数，性能分析指出：此项使用调用极为频繁，若调用属性（函数）将大大拖慢程序
            this._Sign = 1;
            for (int i = 0; i < this._radicalToMatch.Count; i++)
            {
                this._radical = this._radicalToMatch[i];
                var result = this.Match(); // 匹配出结果
                for (int j = 0; j < result.Count; j++)
                {
                    var tempName = (this._radical.Name + this.RecAttribute(this._radical.Tag, result[j])).Replace("*", ""); // 进行属性判断
                    if (!base.DefinedFragment.ContainsKey(tempName)) { base.DefinedFragment.Add(tempName, 0); }
                    base.DefinedFragment[tempName]++; // 装入结果
                }
            }
        }
        public void AddAttribute()
        {
            this._nAtom = base.Molecule.AtomList.Length;
            this._lock = new bool[this._nAtom];
            this._Sign = -1;
            for (int i = 0; i < this._radicalToRename.Count; i++)
            {
                this._radical = this._radicalToRename[i];
                this.Rename(this._radicalToRename[i].SpecialAtom);
                base.Molecule.State = new int[this._nAtom];
            }
        }
        // ---------------------------------------------------------------------------------
        private string RecAttribute(string[] attributeTag, int index)
        {
            string attribute = "";
            for (int i = 0; i < attributeTag.Length; i++)
            {
                if (attributeTag[i][0] != '-')
                {
                    attribute = base.Molecule.AtomList[index].Contains(attributeTag[i]) ? '_' + attributeTag[i] : "";
                }
                else
                {
                    string tempTag = attributeTag[i].Remove(0, 1);
                    if (base.Molecule.AtomList[index].Contains(tempTag)) { continue; } // 1.自己不能含有该属性
                    for (int j = 0; j < base.Molecule.AtomList.Length; j++)
                    {
                        if (this.Molecule.AdjMat[index, j] != 0
                         && base.Molecule.AtomList[j].Contains(tempTag)) // 2.自己所连的原子中，具有该属性
                        {
                            attribute = '_' + attributeTag[i];
                            break;
                        }
                    }
                }
                if (attribute != "") { return attribute; }
            }
            return "";
        }
        private void Rename(int[] renameIndex)
        {
            this.MatchCore(() =>
            {
                //for (int i = 0; i < this._matched.Length; i++) // 全部还原
                //{
                //    base.Molecule.Sign[this._matched[i]] = -1; // 后续原子能用，首原子不能用
                //}
                if (this._radical.Name[0] == '_') // 属性添加
                {
                    for (int i = 0; i < renameIndex.Length; i++)
                    {
                        base.Molecule.AtomList[this._matched[renameIndex[i]]] += this._radical.Name;
                    }
                }
                else // 全名重载
                {
                    for (int i = 0; i < renameIndex.Length; i++)
                    {
                        base.Molecule.AtomList[this._matched[renameIndex[i]]] = this._radical.Name;
                    }
                }
            });
        }
        private List<int> Match()
        {
            var core_List = new List<int>();
            this.MatchCore(() =>
            {
                //for (int i = 0; i < this._radical.SpecialAtom.Length; i++) // 还原SpecialAtom
                //{
                //    base.Molecule.State[this._matched[this._radical.SpecialAtom[i]]] = 0;
                //}
                core_List.Add(this._matched[0]);
                this._Sign++; // 每匹配完一个基团（包括相同的基团第二次匹配）就自增一次
            });
            return core_List;
        }
        // Core -------------------------------------------------------------------------------------
        private void MatchCore(Action operation)
        {
            if (this._nAtom < this._radical.AtomCodeList.Length) { return; }
            this._matched = new int[this._radical.AtomCodeList.Length];
            for (int i = 0; i < this._nAtom; i++)
            {
                if ((base.Molecule.State[i] == 0 || this._radical.SpecialAtom.Contains(0))
                 && this._radical.AtomCodeList[0].IsMatch(base.Molecule.AtomList[i]))
                {
                    this._matched[0] = i;
                    this._isBreak = false;
                    int backupState = base.Molecule.State[this._matched[0]]; // 备份原子访问状态
                    base.Molecule.State[i] = this._Sign;
                    this._lock[i] = true; // 锁定首原子
                    //this._lock = new bool[this._nAtom]; // 没有正在使用中的原子
                    this.Match_R(1);
                    this._lock[i] = false; // 解除锁定
                    // 退出时，将特殊原子全部恢复
                    if (this._radical.Name[0] == '*' && this._radical.SpecialAtom.Contains(0))
                    {
                        base.Molecule.State[i] = backupState;
                    }
                    if (this._isBreak)
                    {
                        operation();
                    }
                    else
                    {
                        base.Molecule.State[i] = backupState; // 当匹配失败时，从备份中还原原子状态
                    }
                }
            }
        }
        private void Match_R(int n)
        {
            if (n == this._matched.Length)
            {
                this._isBreak = true;
                return;
            }
            for (int i = 0; i < n; i++)
            {
                if (this._radical.AdjMat[n - 1][i] == 0) { continue; }
                for (int p_M_Next = 0; p_M_Next < this._nAtom; p_M_Next++)
                {
                    if (base.Molecule.AdjMat[this._matched[i], p_M_Next] != 0
                     && ((base.Molecule.State[p_M_Next] <= 0 || this._radical.SpecialAtom.Contains(n)) && !this._lock[p_M_Next]) // 下个原子的状态码表示可用且该原子并没有正在被使用
                     && this._radical.AtomCodeList[n].IsMatch(base.Molecule.AtomList[p_M_Next]))
                    {
                        this._matched[n] = p_M_Next;
                        if (this.Compare(n, this._matched))
                        {
                            int backupState = base.Molecule.State[this._matched[n]];
                            // 进：修改原子状态 && 锁定原子
                            base.Molecule.State[p_M_Next] = this._Sign;
                            this._lock[p_M_Next] = true;

                            this.Match_R(n + 1);
                            if (this._isBreak)
                            {
                                this._lock[p_M_Next] = false;
                                // 退出时，将特殊原子全部恢复
                                if (this._radical.Name[0] == '*' && this._radical.SpecialAtom.Contains(n))
                                {
                                    base.Molecule.State[p_M_Next] = backupState;
                                }
                                return;
                            }
                            // 退：恢复原子状态 && 接触锁定
                            base.Molecule.State[p_M_Next] = backupState;
                            this._lock[p_M_Next] = false;
                        }
                    }
                }
            }
        }
        private bool Compare(int n, int[] matched)
        {
            for (int j = 0; j < n; j++)
            {
                if (base.Molecule.AdjMat[matched[n], matched[j]] != this._radical.AdjMat[n - 1][j]
               && !(this._radical.AdjMat[n - 1][j] > 4 && base.Molecule.AdjMat[matched[n], matched[j]] != 0))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
