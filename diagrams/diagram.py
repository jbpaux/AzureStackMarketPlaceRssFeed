from diagrams import Diagram, Cluster
from diagrams.onprem.vcs import Github
from diagrams.azure.security import KeyVaults
from diagrams.azure.devops import ApplicationInsights
from diagrams.azure.compute import FunctionApps
from diagrams.azure.storage import StorageAccounts, BlobStorage

graph_attr = {
    "bgcolor": "white",
    "pad": "0.5"
}

with Diagram("Azure Stack Hub MarketPlace RSS Feed", graph_attr=graph_attr, filename="architecture", show=False):
    
    gh = Github("MarketPlace Changelog page")
    
    with Cluster("Azure Services"):
        func = FunctionApps("Functions App")
        appinsight = ApplicationInsights("Application Insights")
        kv = KeyVaults("Keyvault")

        with Cluster("Storage"):
            sc = StorageAccounts("Storage Account")
            blob = BlobStorage("rss/feed.xml")


    gh >> func >> sc
    sc - blob
    appinsight >> func << kv