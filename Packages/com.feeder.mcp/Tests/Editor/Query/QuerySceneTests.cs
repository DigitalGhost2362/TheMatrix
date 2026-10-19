#nullable enable
using System.Linq;
using Feeder.MCP.Editor.Query;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Query
{
    public class QuerySceneTests
    {
        Scene _scene;
        GameObject _root = null!;
        GameObject _lamp = null!;
        GameObject _box = null!;
        GameObject _hidden = null!;
        Material? _material;
        int _undoGroup;

        // the test runner reverts the undo stack after a run, which would bring back objects a test destroyed through Undo;
        // creating the fixture inside an undo group and reverting it in TearDown leaves nothing behind
        [SetUp]
        public void SetUp()
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _scene = SceneManager.GetActiveScene();
            _root = new GameObject("QT_Root");
            _lamp = new GameObject("QT_Lamp");
            _lamp.transform.SetParent(_root.transform);
            var light = _lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 2f;
            _box = new GameObject("QT_Box");
            _box.transform.SetParent(_root.transform);
            _box.transform.position = new Vector3(1, 2, 3);
            _box.AddComponent<BoxCollider>();
            _box.AddComponent<Rigidbody>().mass = 1f;
            _hidden = new GameObject("QT_Hidden");
            _hidden.transform.SetParent(_root.transform);
            _hidden.SetActive(false);
            Undo.RegisterCreatedObjectUndo(_root, "QT fixture");
        }

        [TearDown]
        public void TearDown()
        {
            Undo.RevertAllDownToGroup(_undoGroup);
            if (_material != null)
                Object.DestroyImmediate(_material);
            for (var i = 0; i < SceneManager.sceneCount; i++)
                foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (go.name.StartsWith("QT_"))
                        Object.DestroyImmediate(go);
        }

        static object? Single(string query) => MatrixQuery.Run(query, 1000).Items.Single();
        static int Count(string query) => MatrixQuery.Run(query, 1000).Total;

        #region Reading

        [Test]
        public void Objects_IncludeInactiveAndNested()
        {
            Assert.AreEqual(4L, Single("objects | where name startswith \"QT_\" | count"));
        }

        [Test]
        public void ComponentShortcut_ReadsComponentMembers()
        {
            Assert.AreEqual(2f, Single("find(\"QT_Root/QT_Lamp\") | get Light.intensity"));
            Assert.AreEqual(1L, Single("objects | where name startswith \"QT_\" and Light.type == \"Point\" | count"));
            Assert.AreEqual("QT_Box", Single("objects | where name startswith \"QT_\" and $ is Rigidbody | get name"));
            Assert.AreEqual(true, Single("find(\"QT_Box\") | get has(\"BoxCollider\")"));
        }

        [Test]
        public void Hierarchy_Stages()
        {
            Assert.AreEqual(3, Count("find(\"QT_Root\") | children"));
            Assert.AreEqual("QT_Hidden", Single("find(\"QT_Root\") | descendants | where not activeSelf | get name"));
            Assert.AreEqual("QT_Root", Single("find(\"QT_Box\") | parent | get name"));
            Assert.AreEqual("QT_Root/QT_Box", Single("find(\"QT_Box\") | get $path"));
            CollectionAssert.AreEquivalent(new[] { "Transform", "BoxCollider", "Rigidbody" },
                MatrixQuery.Run("find(\"QT_Box\") | components | select $type").Items.Cast<Row>().Select(r => r.Values[0]).ToArray());
            Assert.AreEqual(1, Count("find(\"QT_Box\") | components Rigidbody | gameobject"));
        }

        [Test]
        public void ComponentShortcut_WarnsWhenSeveralComponentsMatch()
        {
            _box.AddComponent<BoxCollider>().size = new Vector3(2, 2, 2);
            var result = MatrixQuery.Run("find(\"QT_Box\") | get BoxCollider.size");
            Assert.AreEqual(Vector3.one, result.Items.Single());
            StringAssert.Contains("has 2 BoxCollider components", result.Warnings.Single());
            Assert.AreEqual(2, Count("find(\"QT_Box\") | components BoxCollider"));
        }

        [Test]
        public void Receipts_NameTargetsAsTheyWereBeforeTheChange()
        {
            var receipts = MatrixQuery.Mutate("find(\"QT_Box\") | set name = \"QT_Moved\" | call transform.SetParent(find(\"QT_Lamp\").transform)").Receipts;
            Assert.AreEqual("QT_Root/QT_Box", receipts[0].Target);
            Assert.AreEqual("QT_Root/QT_Moved", receipts[1].Target);
        }

        [Test]
        public void FindAndObj_ResolveByPathNameAndId()
        {
            Assert.AreEqual(_box, Single("find(\"QT_Box\")"));
            Assert.AreEqual(_box, Single($"obj({QueryIds.Of(_box)})"));
            Assert.AreEqual(new Vector3(1, 2, 3), Single("find(\"QT_Root/QT_Box\").transform.position"));
            new GameObject("QT_Dup");
            new GameObject("QT_Dup");
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("find(\"QT_Dup\")")).Message, Does.Contain("ambiguous"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("find(\"QT_Nowhere\")")).Message, Does.Contain("found nothing"));
        }

        [Test]
        public void DefaultTable_ShowsPathAndComponents()
        {
            var text = MatrixQuery.Run("find(\"QT_Box\")").Text;
            StringAssert.StartsWith("1 row\nid | path | active | components\n", text);
            StringAssert.Contains("QT_Root/QT_Box | true | BoxCollider, Rigidbody", text);
        }

        #endregion

        #region Changing

        [Test]
        public void Set_WritesThroughStructs()
        {
            MatrixQuery.Mutate("find(\"QT_Box\") | set transform.position.x = 5");
            Assert.AreEqual(new Vector3(5, 2, 3), _box.transform.position);
            MatrixQuery.Mutate("find(\"QT_Box\") | set position.y = 7");
            Assert.AreEqual(new Vector3(5, 7, 3), _box.transform.position);
        }

        [Test]
        public void Set_TwoPartsOfOneStructBothLand()
        {
            MatrixQuery.Mutate("find(\"QT_Box\") | set transform.position.x = 10, transform.position.z = 30");
            Assert.AreEqual(new Vector3(10, 2, 30), _box.transform.position);
        }

        [Test]
        public void Set_RightHandSidesSeeValuesBeforeTheChange()
        {
            MatrixQuery.Mutate("find(\"QT_Box\") | set Rigidbody.mass = Rigidbody.mass * 3");
            Assert.AreEqual(3f, _box.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void Set_ConvertsEnumsColorsAndAliases()
        {
            MatrixQuery.Mutate("find(\"QT_Lamp\") | set Light.type = \"Spot\", Light.color = \"#00ff00\", Light.intensity = 4");
            var light = _lamp.GetComponent<Light>();
            Assert.AreEqual(LightType.Spot, light.type);
            Assert.AreEqual(Color.green, light.color);
            Assert.AreEqual(4f, light.intensity);

            MatrixQuery.Mutate("find(\"QT_Box\") | set parent = find(\"QT_Lamp\"), localPosition = [0, 1, 0]");
            Assert.AreEqual(_lamp.transform, _box.transform.parent);
            Assert.AreEqual(new Vector3(0, 1, 0), _box.transform.localPosition);

            MatrixQuery.Mutate("find(\"QT_Lamp\") | set active = false");
            Assert.IsFalse(_lamp.activeSelf);
        }

        [Test]
        public void Set_IsAllOrNothing()
        {
            var error = Assert.Throws<QueryException>(() => MatrixQuery.Mutate("objects | where name startswith \"QT_\" | set Light.intensity = 9"));
            StringAssert.Contains("Nothing was changed", error.Message);
            Assert.AreEqual(2f, _lamp.GetComponent<Light>().intensity);
        }

        [Test]
        public void Set_RefusesReadOnlyMembers()
        {
            var error = Assert.Throws<QueryException>(() => MatrixQuery.Mutate("find(\"QT_Root\") | set transform.childCount = 1"));
            StringAssert.Contains("read-only", error.Message);
        }

        [Test]
        public void DryRun_ReportsButChangesNothing()
        {
            var result = MatrixQuery.Mutate("find(\"QT_Box\") | set name = \"Renamed\"", dryRun: true);
            Assert.AreEqual("QT_Box", _box.name);
            var receipt = result.Receipts.Single();
            Assert.AreEqual("QT_Root/QT_Box", receipt.Target);
            Assert.AreEqual("QT_Box", receipt.Old);
            Assert.AreEqual("Renamed", receipt.New);
            StringAssert.StartsWith("Dry run: nothing was changed.", result.Text);
        }

        [Test]
        public void DryRun_RevertsCreatedObjects()
        {
            MatrixQuery.Mutate("new(\"QT_Ghost\", \"Cube\")", dryRun: true);
            Assert.IsFalse(_scene.GetRootGameObjects().Any(go => go != null && go.name == "QT_Ghost"));
        }

        [Test]
        public void Mutation_IsOneUndoStep()
        {
            MatrixQuery.Mutate("find(\"QT_Root\") | children | set name = name + \"_x\"");
            Assert.AreEqual("QT_Box_x", _box.name);
            Assert.AreEqual("QT_Lamp_x", _lamp.name);
            Undo.PerformUndo();
            Assert.AreEqual("QT_Box", _box.name);
            Assert.AreEqual("QT_Lamp", _lamp.name);
            Assert.AreEqual("QT_Hidden", _hidden.name);
        }

        [Test]
        public void LaterStageFailure_RollsBackEarlierStages()
        {
            var error = Assert.Throws<QueryException>(() => MatrixQuery.Mutate("find(\"QT_Box\") | set name = \"QT_Changed\" | call NoSuchMethod()"));
            StringAssert.Contains("rolled back", error.Message);
            Assert.AreEqual("QT_Box", _box.name);
        }

        [Test]
        public void MaxTargets_RefusesWideChanges()
        {
            var error = Assert.Throws<QueryException>(() => MatrixQuery.Mutate("objects | where name startswith \"QT_\" | set name = name", maxTargets: 2));
            StringAssert.Contains("maxTargets", error.Message);
        }

        [Test]
        public void Add_ThenSetOnTheNewComponent()
        {
            MatrixQuery.Mutate("find(\"QT_Lamp\") | add Rigidbody | set mass = 4");
            Assert.AreEqual(4f, _lamp.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void Destroy_RemovesObjectsAndRefusesTransforms()
        {
            MatrixQuery.Mutate("find(\"QT_Hidden\") | destroy");
            Assert.IsTrue(_hidden == null);
            Assert.Throws<QueryException>(() => MatrixQuery.Mutate("find(\"QT_Box\") | components Transform | destroy"));
            MatrixQuery.Mutate("find(\"QT_Box\") | components BoxCollider | destroy");
            Assert.IsNull(_box.GetComponent<BoxCollider>());
        }

        [Test]
        public void Call_RunsMethodsOnTargetsAndMembers()
        {
            MatrixQuery.Mutate("find(\"QT_Box\") | call transform.SetParent(find(\"QT_Lamp\").transform)");
            Assert.AreEqual(_lamp.transform, _box.transform.parent);
            MatrixQuery.Mutate("find(\"QT_Box\") | call SetActive(false)");
            Assert.IsFalse(_box.activeSelf);
        }

        [Test]
        public void New_CreatesUnderAParent()
        {
            MatrixQuery.Mutate("new(\"QT_New\", \"Cube\", \"QT_Root\") | set position = [1, 2, 3]");
            var created = _root.transform.Find("QT_New");
            Assert.IsNotNull(created);
            Assert.IsNotNull(created.GetComponent<MeshFilter>());
            Assert.AreEqual(new Vector3(1, 2, 3), created.position);
        }

        [Test]
        public void ShaderProperties_ReadAndWriteByName()
        {
            _material = new Material(Shader.Find("Sprites/Default"));
            var id = QueryIds.Of(_material);
            MatrixQuery.Mutate($"obj({id}) | set $[\"_Color\"] = \"#ff0000\"");
            Assert.AreEqual(Color.red, _material.GetColor("_Color"));
            Assert.AreEqual(Color.red, Single($"obj({id}) | get $[\"_Color\"]"));
        }

        #endregion

        #region Schema

        [Test]
        public void Schema_DescribesTypesAndTheLanguage()
        {
            StringAssert.Contains("intensity | Single | get set", QuerySchema.Describe("Light"));
            StringAssert.Contains("values: ", QuerySchema.Describe("LightType"));
            var overview = QuerySchema.Describe();
            StringAssert.Contains("where EXPR", overview);
            StringAssert.Contains("Light ", overview);
            Assert.Throws<QueryException>(() => QuerySchema.Describe("NoSuchTypeAnywhere"));
        }

        #endregion
    }
}
