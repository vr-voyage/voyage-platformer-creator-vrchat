
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class LevelParser : UdonSharpBehaviour
{
    [TextArea]
    public string jsonData;
    public int[] ids;
    public Mesh[] buildingBlocks;
    public MeshFilter mapStatic;
    public MeshCollider meshCollider;

    bool ContentIsValid(DataToken parsedJson)
    {
        if (parsedJson.TokenType != TokenType.DataList)
        {
            Debug.LogError("Invalid data type");
            return false;
        }

        DataList datalist = (DataList)parsedJson;

        /* Type checks */
        int datalistLength = datalist.Count;
        for (int data = 0; data < datalistLength; data++)
        {
            DataToken element = datalist[data];
            if (element.TokenType != TokenType.DataList)
            {
                Debug.LogError($"Invalid element type for {element} ([{data}]). Expected DataList but got {element.TokenType}");
                return false;
            }

            DataList blockInfo = (DataList)element;
            int blockInfoCount = blockInfo.Count;

            if (blockInfoCount != 2)
            {
                Debug.LogError(
                    $"Unexpected array size of block data at {data}. Expected 2 got {blockInfoCount}");
                return false;
            }

            for (int blockData = 0; blockData < blockInfoCount; blockData++)
            {
                DataToken idListOrPos = blockInfo[blockData];

                if (idListOrPos.TokenType != TokenType.DataList)
                {
                    Debug.LogError($"Invalid type for block data [{data}][{blockData}]. Expected a list but got {idListOrPos.TokenType}");
                    return false;
                }

                DataList idListOrPosList = (DataList)idListOrPos;
                int coordinateCount = idListOrPosList.Count;
                if (coordinateCount != 2)
                {
                    Debug.LogError($"Unexpected array size for block data at [{data}]{blockData}]. Expected 2 got {coordinateCount}");
                    return false;
                }



                for (int coordinate = 0; coordinate < coordinateCount; coordinate++)
                {
                    DataToken intValue = idListOrPosList[coordinate];
                    if (intValue.TokenType != TokenType.Double)
                    {
                        Debug.Log($"Expected double values for the final values. Got {intValue.TokenType}");
                        return false;
                    }
                }
            }

            DataList blockID = (DataList)(blockInfo[0]);
            int localID = JsonIdToLocalId((DataList)blockInfo[0]);
            if (FindID(localID) < 0)
            {
                Debug.Log($"Invalid ID found at [{data}][0] (Local : {localID}. Distant : {(int)blockID[0]} {(int)blockID[1]})");
                return false;
            }
        }

        return true;
    }

    public void OnEnable()
    {
        if (buildingBlocks.Length != ids.Length)
        {
            Debug.LogError("Building blocks and IDS differ !");
            return;
        }
    }

    int JsonIdToLocalId(DataList jsonId)
    {
        double x = ((double)jsonId[0]);
        double y = ((double)jsonId[1]);
        int idX = (((int)x) & 0xffff) <<  0;
        int idY = (((int)y) & 0xffff) << 16;
        return (idX | idY);
    }

    int FindID(int localId)
    {
        int[] localIds = ids;
        for (int i = 0; i < ids.Length; i++)
        {
           if (localIds[i] == localId)
           {
               return i;
           }
        }
        return -1;
    }

    int AddObject(
        int objectIndex, Vector3 position,
        CombineInstance[] combineInstances, int index)
    {
        if ((objectIndex < 0) | (objectIndex >= buildingBlocks.Length))
        {
            Debug.LogWarning($"Invalid object index : {objectIndex}");
            return -1;
        }

        if (index >= combineInstances.Length)
        {
            Debug.LogWarning($"Too much combineInstances ! {index} >= {combineInstances.Length}");
            return -1;
        }

        Mesh targetMesh = buildingBlocks[objectIndex];

        Debug.Log($"Adding {targetMesh.name} at [{position}]");

        if (index + targetMesh.subMeshCount > combineInstances.Length)
        {
            return -1;
        }

        int combineInstanceIndex = index;


        for (int submesh = 0; submesh < targetMesh.subMeshCount; submesh++)
        {
            CombineInstance addedInstance = new CombineInstance();
            addedInstance.mesh = targetMesh;
            addedInstance.subMeshIndex = submesh;
            addedInstance.transform = Matrix4x4.Translate(position);
            combineInstances[combineInstanceIndex] = addedInstance;
            combineInstanceIndex++;

        }

        return combineInstanceIndex;

    }

    int MaxSubmeshes(Mesh[] meshes)
    {
        if (meshes == null)
        {
            return 0;
        }

        int max = 0;

        for (int mesh = 0; mesh < meshes.Length; mesh++)
        {
            max = Mathf.Max(meshes[mesh].subMeshCount, max);
        }
        return max;
        
    }

    void BuildMap(DataList jsonArray)
    {
        int nBlocks = jsonArray.Count;
        int materialsSum = MaxSubmeshes(buildingBlocks) * nBlocks;

        if (materialsSum < 0)
        {
            return;
        }

        CombineInstance[] combineInstances = new CombineInstance[materialsSum];
        int combineInstanceIndex = 0;

        
        Debug.Log($"<color=orange>Number of blocks : {nBlocks}</color>");

        for (int blockIndex = 0; blockIndex < nBlocks; blockIndex++)
        {
            Debug.Log($"<color=green>Block {blockIndex}");
            DataList block = (DataList) jsonArray[blockIndex];

            DataList id = (DataList)block[0];
            DataList position = (DataList)block[1];
            int localID = JsonIdToLocalId(id);
            Vector3 blockPosition = new Vector3((float)((double)position[0]), -(float)((double)position[1]), 0);

            int returnedIndex = AddObject(
                objectIndex: FindID(localID),
                position: blockPosition,
                combineInstances, combineInstanceIndex);
            if (returnedIndex >= 0)
            {
                combineInstanceIndex = returnedIndex;
            }

            
        }
        CombineInstance[] actualInstances = combineInstances;
        if (combineInstanceIndex < combineInstances.Length)
        {
            actualInstances = new CombineInstance[combineInstanceIndex];
            for (int instance = 0; instance < combineInstanceIndex; instance++)
            {
                actualInstances[instance] = combineInstances[instance];
            }
        }
        var newMapMesh = new Mesh();
        newMapMesh.CombineMeshes(actualInstances, true, true, false);
        mapStatic.sharedMesh = newMapMesh;
        meshCollider.sharedMesh = newMapMesh;
    }

    void Start()
    {
        bool parsed = VRCJson.TryDeserializeFromJson(jsonData, out DataToken parsedJson);
        if (!parsed)
        {
            Debug.LogError("Could not parse the provided json data");
            Debug.Log(parsedJson);
            return;
        }

        if (!ContentIsValid(parsedJson))
        {
            Debug.LogError("The provided JSON data had invalid fields");
            return;
        }

        BuildMap((DataList)parsedJson);
        

    }
}
