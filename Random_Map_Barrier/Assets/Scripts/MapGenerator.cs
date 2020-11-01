﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour {
    public Map[] maps;
    public int mapIndex;
    public Transform tilePrefab;//地图预制体
    public Transform obsPrefab;
    public Transform navmeshFloor;
    public Transform navMeshObs;
    public Vector2 mapMaxSize;

    [Range(0, 1)]
    public float outlinePercent;//地图间隔

    public float tileSize;
    List<Coord> allTileCoords;//存储所有地图贴片的坐标信息数组
    Queue<Coord> shuffledTileCoords;//存储洗牌算法生成的乱序队列

    Map currentMap;


    void Start() {
        GenerateMap();
    }

    /// <summary>
    /// 地图生成算法
    /// </summary>
    public void GenerateMap() {
        currentMap = maps[mapIndex];
        System.Random random = new System.Random(currentMap.seed);
        //GetComponent<BoxCollider>().size = new Vector3(currentMap.mapSize.x * tileSize, .05f, currentMap.mapSize.y * tileSize);

        //存储地图坐标数据
        allTileCoords = new List<Coord>();
        for (int x = 0; x < currentMap.mapSize.x; x++) {
            for (int y = 0; y < currentMap.mapSize.y; y++) {
                allTileCoords.Add(new Coord(x, y));
            }
        }
        //洗牌
        shuffledTileCoords = new Queue<Coord>(Utils.ShuffleCoords<Coord>(allTileCoords.ToArray(), currentMap.seed));

        //先销毁旧的地图,若存在
        string holderName = "MapHolder";
        if (transform.Find(holderName)) {
            GameObject.DestroyImmediate(transform.Find(holderName).gameObject);
        }
        Transform mapHolder = new GameObject(holderName).transform;
        mapHolder.parent = transform;

        //生成给定尺寸大小的地图
        for (int x = 0; x < currentMap.mapSize.x; x++) {
            for (int y = 0; y < currentMap.mapSize.y; y++) {
                Vector3 tilePos = CoordToPosition(x, y);//计算位置
                Transform newTile = GameObject.Instantiate(tilePrefab, tilePos, Quaternion.Euler(Vector3.right * 90)) as Transform;
                newTile.localScale = Vector3.one * (1 - outlinePercent) * tileSize;//缩放以达到间隔
                newTile.parent = mapHolder;
            }
        }
        //TODO:限制生成闭合地图的障碍物
        bool[,] obsMap = new bool[(int)currentMap.mapSize.x, (int)currentMap.mapSize.y];
        int curObsCount = 0;
        //生成障碍物
        int obsCount = (int)Mathf.Ceil(currentMap.obsPercent * currentMap.mapSize.x * currentMap.mapSize.y);
        for (int i = 0; i < obsCount; i++) {
            Coord randomCoord = GetRandomCoord();//获取随机坐标作为障碍物的生成坐标
            obsMap[randomCoord.x, randomCoord.y] = true;//默认可以生成障碍物
            curObsCount++;
            if (randomCoord != currentMap.mapCenter && MapIsFullyAccessible(obsMap, curObsCount)) {
                //随机高度
                float obsHeight = Mathf.Lerp(currentMap.minObsHeight, currentMap.maxObsHeight, (float)random.NextDouble());
                Vector3 obsPos = CoordToPosition(randomCoord.x, randomCoord.y);
                //实例化,缩放,位置,父节点,旋转角度
                Transform newObs = Instantiate(obsPrefab, obsPos + Vector3.up * obsHeight / 2, Quaternion.identity) as Transform;
                newObs.parent = mapHolder;
                newObs.localScale = new Vector3((1 - outlinePercent) * tileSize, obsHeight, (1 - outlinePercent) * tileSize);

                Renderer obsRenderer = newObs.GetComponent<Renderer>();
                Material obsMaterial = new Material(obsRenderer.sharedMaterial);
                float colorPercent = randomCoord.y / (float)currentMap.mapSize.y;
                obsMaterial.color = Color.Lerp(currentMap.foregroundColor, currentMap.backgroundColor, colorPercent);
                obsRenderer.sharedMaterial = obsMaterial;
            } else {
                curObsCount--;
                obsMap[randomCoord.x, randomCoord.y] = false;
            }
        }

        // TODO: 动态生成'空气墙'NavMeshObstacle + Collider
        Transform maskLeft = Instantiate(navMeshObs, new Vector3(0.5f, 0.5f, 0) + Vector3.left * (currentMap.mapSize.x + mapMaxSize.x) / 4 * tileSize, Quaternion.identity) as Transform;
        maskLeft.parent = mapHolder;
        maskLeft.localScale = new Vector3((mapMaxSize.x - currentMap.mapSize.x) / 2, 1, currentMap.mapSize.y) * tileSize;

        Transform maskRight = Instantiate(navMeshObs, new Vector3(0.5f, 0.5f, 0) + Vector3.right * (currentMap.mapSize.x + mapMaxSize.x) / 4 * tileSize, Quaternion.identity) as Transform;
        maskRight.parent = mapHolder;
        maskRight.localScale = new Vector3((mapMaxSize.x - currentMap.mapSize.x) / 2, 1, currentMap.mapSize.y) * tileSize;

        Transform maskTop = Instantiate(navMeshObs, new Vector3(0.5f, 0.5f, 0) + Vector3.forward * (currentMap.mapSize.y + mapMaxSize.y) / 4 * tileSize, Quaternion.identity) as Transform;
        maskTop.parent = mapHolder;
        maskTop.localScale = new Vector3(mapMaxSize.x, 1, (mapMaxSize.y - currentMap.mapSize.y) / 2) * tileSize;

        Transform maskBottom = Instantiate(navMeshObs, new Vector3(0.5f, 0.5f, 0) + Vector3.back * (currentMap.mapSize.y + mapMaxSize.y) / 4 * tileSize, Quaternion.identity) as Transform;
        maskBottom.parent = mapHolder;
        maskBottom.localScale = new Vector3(mapMaxSize.x, 1, (mapMaxSize.y - currentMap.mapSize.y) / 2) * tileSize;

        navmeshFloor.localScale = new Vector3(mapMaxSize.x, mapMaxSize.y) * tileSize;
    }

    /// <summary>
    /// 填充判断算法,检测地图是否可以生成障碍物
    /// </summary>
    /// <returns></returns>
    private bool MapIsFullyAccessible(bool[,] mapObsInfo, int curObsCount) {
        bool[,] mapFlag = new bool[mapObsInfo.GetLength(0), mapObsInfo.GetLength(1)];//初始化标记信息
        Queue<Coord> queue = new Queue<Coord>(); //所有通过筛选的地图会存放到这个队列中
        queue.Enqueue(currentMap.mapCenter);
        mapFlag[(int)currentMap.mapCenter.x, (int)currentMap.mapCenter.y] = true;//中心点一定不能有障碍物
        int accessibleCount = 1;
        while (queue.Count > 0) {
            Coord curTile = queue.Dequeue();
            //四领域填充算法
            for (int i = -1; i <= 1; i++) {
                for (int j = -1; j <= 1; j++) {
                    //八个位置信息
                    int neighborX = curTile.x + i;
                    int neighborY = curTile.y + j;
                    //筛选出检测的坐标信息
                    if (i == 0 || j == 0) {
                        if (neighborX >= 0 && neighborX < mapObsInfo.GetLength(0) && neighborY >= 0 && neighborY < mapObsInfo.GetLength(1)) {
                            //当前坐标没有被检测过,并且不存在障碍物
                            if (!mapFlag[neighborX, neighborY] && !mapObsInfo[neighborX, neighborY]) {
                                mapFlag[neighborX, neighborY] = true;
                                accessibleCount++;
                                queue.Enqueue(new Coord(neighborX, neighborY));
                            }
                        }
                    }
                }
            }
        }

        int targetAccessibleCount = (int)(currentMap.mapSize.x * currentMap.mapSize.y - curObsCount);
        return targetAccessibleCount == accessibleCount;
    }

    /// <summary>
    /// 将坐标数据转化为Position信息
    /// </summary>
    /// <returns></returns>
    public Vector3 CoordToPosition(int x, int y) {
        return new Vector3(-currentMap.mapSize.x / 2 + 0.5f + x, 0, -currentMap.mapSize.y / 2 + 0.5f + y) * tileSize;
    }

    /// <summary>
    /// 获取随机的Coord对象
    /// </summary>
    /// <returns></returns>
    public Coord GetRandomCoord() {
        //将队列的首元素放入队尾
        Coord randomCoord = shuffledTileCoords.Dequeue();//出队
        shuffledTileCoords.Enqueue(randomCoord);//入队

        return randomCoord;
    }

    [System.Serializable]
    public struct Coord {
        public int x;
        public int y;
        public Coord(int _x, int _y) {
            x = _x;
            y = _y;
        }
        //重定义操作符,必须static并且返回bool值,一般重定义操作符都是成对重定义
        public static bool operator ==(Coord c1, Coord c2) {
            return c1.x == c2.x && c1.y == c2.y;
        }
        public static bool operator !=(Coord c1, Coord c2) {
            return !(c1 == c2);
        }
    }
    [System.Serializable]
    public class Map {
        public Coord mapSize;//地图尺寸
        [Range(0, 1)]
        public float obsPercent;//障碍物所占百分比
        public int seed;//随机种子
        public float minObsHeight;//障碍物最小高度
        public float maxObsHeight;//障碍物最大高度
        public Color foregroundColor;//前景色
        public Color backgroundColor;//背景色

        //地图的中心点,每个随机地图的中心点都是不能有障碍物的
        public Coord mapCenter {
            get {
                return new Coord(mapSize.x / 2, mapSize.y / 2);
            }
        }

    }
}

