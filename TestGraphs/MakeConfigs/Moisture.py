# ---
# jupyter:
#   jupytext:
#     formats: ipynb,py:light
#     text_representation:
#       extension: .py
#       format_name: light
#       format_version: '1.5'
#       jupytext_version: 1.15.0
#   kernelspec:
#     display_name: Python 3 (ipykernel)
#     language: python
#     name: python3
# ---

import os 
import pandas as pd

if os.getenv("GITHUB_ACTIONS") == True:
    root = os.environ["GITHUB_WORKSPACE"]
    inPath = os.path.join(root, "TestComponents", "TestSets", "Moisture", "Outputs")
    outPath = os.path.join(root, "TestGraphs", "Outputs")  
else: 
    rootfrags = os.path.abspath('WS2.ipynb').split("\\")
    root = ""
    for d in rootfrags:
        if d == "FieldNBalance":
            break
        else:
            root += d + "\\"
    path = os.path.join(root,"FieldNBalance","TestComponents", "TestSets", "Moisture")

Configs = pd.read_excel(
    os.path.join(path, "FieldConfigs.xlsx"),
    nrows=45,
    usecols=lambda x: 'Unnamed' not in x,
    keep_default_na=False)

Configs.set_index('Name',inplace=True)

CSConfigs = Configs.transpose()
CSConfigs.to_csv(os.path.join(path, "FieldConfigs.csv"),header=True)

Configs.to_pickle(os.path.join(path, "FieldConfigs.pkl"))
